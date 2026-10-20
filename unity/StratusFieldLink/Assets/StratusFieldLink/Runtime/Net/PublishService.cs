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
        FailedLocalValidation = 4,
        QueuedForRetry = 5
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

        public PublishQueue Queue { get; set; }

        /// <summary>Drain the offline queue when the component starts. Tests turn this off.</summary>
        public bool DrainOnStart = true;

        public const int MaxRetries = 3;

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

        private IEnumerator Start()
        {
            if (DrainOnStart)
            {
                yield return DrainQueue();
            }
        }

        private bool _draining;

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

            // What happens to each answer (see the unit tests for the contract):
            //   2xx            sent. 200 = server already had it (duplicate, still a success).
            //   no signal      kept on the tablet; the drain stops, nothing is lost.
            //   5xx            kept and retried, at most MaxRetries times, then held.
            //   4xx            the foreman is told why (RejectedByServer); kept and retried like
            //                  5xx, at most MaxRetries times, then held.
            //   409            kept but held at once (same id, different content): never auto-resent.
            //   Held sketches stay on the tablet; republishing under the same id replaces them.
            if (response.IsSuccess)
            {
                // 200 means the server already had this exact sketch; 201 means it is new.
                var duplicate = response.StatusCode == 200;
                if (Queue.Remove(sketch.id))
                {
                    Queue.Save();
                }

                var sent = new PublishResult
                {
                    Outcome = PublishOutcome.Succeeded,
                    Message = duplicate
                        ? "Already published (duplicate): " + sketch.id
                        : "Published " + sketch.id,
                    StatusCode = response.StatusCode,
                    IsDuplicate = duplicate
                };
                Complete(sent);

                // A 2xx proves we have signal: send whatever was waiting.
                if (!_draining)
                {
                    yield return DrainQueue();
                    LastResult = sent;
                }

                yield break;
            }

            Debug.LogWarning("[FieldLink] publish did not land: " +
                             (response.IsNetworkError ? response.Error : response.StatusCode + " " + response.Body));

            var wasQueued = Queue.Contains(sketch.id);

            if (response.IsNetworkError)
            {
                KeepQueued(sketch, wasQueued);
                Complete(new PublishResult
                {
                    Outcome = PublishOutcome.QueuedOffline,
                    Message = "No signal. Saved on this tablet and will send when back in range (" + Queue.Count + " waiting).",
                    StatusCode = 0
                });
                yield break;
            }

            if (response.StatusCode >= 500)
            {
                if (wasQueued)
                {
                    sketch.retryCount++;
                }

                var gaveUp = sketch.retryCount >= MaxRetries;
                if (gaveUp)
                {
                    sketch.holdReason = "Server error " + response.StatusCode + "; gave up after " + MaxRetries + " retries.";
                }

                KeepQueued(sketch, wasQueued);
                Complete(new PublishResult
                {
                    Outcome = PublishOutcome.QueuedForRetry,
                    Message = gaveUp
                        ? "Server keeps failing (HTTP " + response.StatusCode + "). Gave up after " + MaxRetries + " retries; the sketch is kept on this tablet."
                        : "Server error (HTTP " + response.StatusCode + "). Saved; will retry (" + sketch.retryCount + " of " + MaxRetries + " retries used).",
                    StatusCode = response.StatusCode
                });
                yield break;
            }

            var reason = ApiErrorDto.Describe(response.Body);
            var message = string.IsNullOrEmpty(reason) ? "Publish failed (HTTP " + response.StatusCode + ")." : reason;
            var conflict = response.StatusCode == 409;
            if (wasQueued && !conflict)
            {
                sketch.retryCount++;
            }

            var held = conflict || sketch.retryCount >= MaxRetries;
            if (held)
            {
                sketch.holdReason = message;
            }

            KeepQueued(sketch, wasQueued);
            Complete(new PublishResult
            {
                Outcome = PublishOutcome.RejectedByServer,
                Message = held ? message + " Kept on this tablet." : message + " Saved; will retry (" + sketch.retryCount + " of " + MaxRetries + " retries used).",
                StatusCode = response.StatusCode
            });
        }

        /// <summary>
        /// Sends every queued sketch that is not held, oldest first. Stops at the first
        /// network failure (no point hammering a dead link). Runs at start-up and after
        /// any successful publish; safe to call at any time.
        /// </summary>
        public IEnumerator DrainQueue()
        {
            if (_draining)
            {
                yield break;
            }

            _draining = true;
            try
            {
                foreach (var sketch in Queue.Drainable())
                {
                    yield return PublishRoutine(sketch);
                    if (LastResult.Outcome == PublishOutcome.QueuedOffline)
                    {
                        yield break;
                    }
                }
            }
            finally
            {
                _draining = false;
            }
        }

        private void KeepQueued(FieldSketchDto sketch, bool alreadyQueued)
        {
            if (!alreadyQueued)
            {
                Queue.Enqueue(sketch);
            }

            Queue.Save();
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
