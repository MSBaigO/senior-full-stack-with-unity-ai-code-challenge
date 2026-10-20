using System;
using System.Collections;
using UnityEngine;
using StratusFieldLink.Config;
using StratusFieldLink.Geometry;
using StratusFieldLink.Model;

namespace StratusFieldLink.Net
{
    public enum PublishOutcome
    {
        None = 0,
        Succeeded = 1,
        RejectedByServer = 2,
        QueuedOffline = 3,
        FailedLocalValidation = 4
    }

    public struct PublishResult
    {
        public PublishOutcome Outcome;
        public string Message;
        public long StatusCode;

        /// <summary>True when the server already had this exact sketch (HTTP 200). Still a success.</summary>
        public bool IsDuplicate;

        public bool IsSuccess
        {
            get { return Outcome == PublishOutcome.Succeeded; }
        }
    }

    /// <summary>
    /// Owns the client-to-cloud path for a sketch: validate, send, and decide what
    /// happens when the send does not land.
    /// </summary>
    public sealed class PublishService : MonoBehaviour
    {
        public const string PublishPath = "/api/sketches";

        /// <summary>Swap this in tests. Defaults to a real UnityWebRequest transport.</summary>
        public IFieldLinkTransport Transport { get; set; }

        public PublishQueue Queue { get; private set; }

        public PublishResult LastResult { get; private set; }

        public bool IsBusy { get; private set; }

        public event Action<PublishResult> PublishCompleted;

        private void Awake()
        {
            if (Transport == null)
            {
                Transport = new UnityWebRequestTransport();
            }

            if (Queue == null)
            {
                Queue = new PublishQueue();
                Queue.Load();
            }
        }

        public Coroutine Publish(FieldSketchDto sketch)
        {
            return StartCoroutine(PublishRoutine(sketch));
        }

        public IEnumerator PublishRoutine(FieldSketchDto sketch)
        {
            IsBusy = true;

            var validation = SketchValidation.Validate(sketch);
            if (!validation.IsValid)
            {
                Complete(new PublishResult
                {
                    Outcome = PublishOutcome.FailedLocalValidation,
                    Message = validation.Message
                });
                yield break;
            }

            sketch.clientLengthFeet = SketchGeometry.TotalLengthFeet(sketch.nodes);
            sketch.clientVersion = FieldLinkConfig.ClientVersion;

            var json = JsonUtility.ToJson(sketch);
            var url = FieldLinkConfig.Url(PublishPath);

            var response = default(TransportResponse);
            yield return Transport.PostJson(url, json, r => response = r);

            // -----------------------------------------------------------------
            // TODO(candidate): this is the seam the role is actually about.
            //
            // Right now anything that is not a 2xx is dropped on the floor with a
            // log line. Decide and implement what should happen for:
            //   - no signal / timeout       (the tablet is in a basement)
            //   - 4xx from the server       (the sketch is wrong; the foreman must know)
            //   - 5xx / transient           (retry, but not forever)
            //   - the same sketch published twice (id is client-generated for a reason)
            //
            // PublishQueue is written and unit-tested but nothing drains it.
            // -----------------------------------------------------------------
            if (response.IsSuccess)
            {
                // 200 means the server already had this exact sketch; 201 means it is new.
                var duplicate = response.StatusCode == 200;
                Complete(new PublishResult
                {
                    Outcome = PublishOutcome.Succeeded,
                    Message = duplicate
                        ? "Already published (duplicate): " + sketch.id
                        : "Published " + sketch.id,
                    StatusCode = response.StatusCode,
                    IsDuplicate = duplicate
                });
                yield break;
            }

            Debug.LogWarning("[FieldLink] publish did not land: " +
                             (response.IsNetworkError ? response.Error : response.StatusCode + " " + response.Body));

            var reason = ApiErrorDto.Describe(response.Body);
            Complete(new PublishResult
            {
                Outcome = PublishOutcome.RejectedByServer,
                Message = string.IsNullOrEmpty(reason) ? "Publish failed (HTTP " + response.StatusCode + ")." : reason,
                StatusCode = response.StatusCode
            });
        }

        /// <summary>
        /// TODO(candidate): nothing calls this yet. Something should, when the
        /// tablet comes back into signal.
        /// </summary>
        public IEnumerator DrainQueue()
        {
            yield break;
        }

        private void Complete(PublishResult result)
        {
            LastResult = result;
            IsBusy = false;

            var handler = PublishCompleted;
            if (handler != null)
            {
                handler(result);
            }
        }
    }
}
