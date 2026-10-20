using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using StratusFieldLink.Model;
using StratusFieldLink.Net;

namespace StratusFieldLink.Tests.PlayMode
{
    public class PublishFlowTests
    {
        private GameObject _host;
        private PublishService _publisher;
        private StubFieldLinkTransport _transport;
        private const string TestKey = "stratus.fieldlink.tests.playmode.pending";

        [SetUp]
        public void SetUp()
        {
            _host = new GameObject("PublishServiceHost");
            _publisher = _host.AddComponent<PublishService>();
            _transport = new StubFieldLinkTransport();
            _publisher.Transport = _transport;
            _publisher.DrainOnStart = false;
            PlayerPrefs.DeleteKey(TestKey);
            _publisher.Queue = new PublishQueue(TestKey);
        }

        [TearDown]
        public void TearDown()
        {
            PlayerPrefs.DeleteKey(TestKey);
            if (_host != null)
            {
                Object.Destroy(_host);
            }
        }

        private static FieldSketchDto Sketch()
        {
            var sketch = FieldSketchDto.NewDraft("PKG-CHW-L02", "CREW-04");
            sketch.nodes.Add(new SketchNodeDto(new Vector3(0f, 10f, 0f)));
            sketch.nodes.Add(new SketchNodeDto(new Vector3(36f, 10f, 0f)));
            return sketch;
        }

        [UnityTest]
        public IEnumerator Publish_WhenServerAccepts_ReportsSuccess()
        {
            _transport.NextResponse = StubFieldLinkTransport.Status(201, "{\"id\":\"SK-1\"}");

            yield return _publisher.PublishRoutine(Sketch());

            Assert.IsTrue(_publisher.LastResult.IsSuccess, _publisher.LastResult.Message);
            Assert.AreEqual(1, _transport.CallCount);
        }

        [UnityTest]
        public IEnumerator Publish_SendsTheSketchAsJsonToThePublishEndpoint()
        {
            var sketch = Sketch();

            yield return _publisher.PublishRoutine(sketch);

            StringAssert.EndsWith(PublishService.PublishPath, _transport.SentUrls[0]);
            StringAssert.Contains(sketch.id, _transport.SentBodies[0]);
            StringAssert.Contains("\"packageId\"", _transport.SentBodies[0]);
            StringAssert.Contains("\"nodes\"", _transport.SentBodies[0]);
        }

        [UnityTest]
        public IEnumerator Publish_StampsTheClientMeasuredLength()
        {
            var sketch = Sketch();

            yield return _publisher.PublishRoutine(sketch);

            Assert.AreEqual(36f, sketch.clientLengthFeet, 0.001f);
        }

        [UnityTest]
        public IEnumerator Publish_SketchThatFailsLocalValidation_NeverHitsTheNetwork()
        {
            var sketch = FieldSketchDto.NewDraft("PKG-CHW-L02", "CREW-04");
            sketch.nodes.Add(new SketchNodeDto(new Vector3(0f, 10f, 0f)));

            yield return _publisher.PublishRoutine(sketch);

            Assert.AreEqual(PublishOutcome.FailedLocalValidation, _publisher.LastResult.Outcome);
            Assert.AreEqual(0, _transport.CallCount);
        }

        [UnityTest]
        public IEnumerator Publish_VerticalDrop_IsSentWithItsTrueLength()
        {
            var sketch = FieldSketchDto.NewDraft("PKG-CHW-L03", "CREW-04");
            sketch.nodes.Add(new SketchNodeDto(new Vector3(0f, 22f, 0f)));
            sketch.nodes.Add(new SketchNodeDto(new Vector3(0f, 10f, 0f)));

            yield return _publisher.PublishRoutine(sketch);

            Assert.AreEqual(1, _transport.CallCount, _publisher.LastResult.Message);
            Assert.AreEqual(12f, sketch.clientLengthFeet, 0.001f);
        }

        [UnityTest]
        public IEnumerator Publish_ServerSaysItAlreadyHasIt_IsSuccessFlaggedAsDuplicate()
        {
            _transport.NextResponse = StubFieldLinkTransport.Status(200, "{\"id\":\"SK-1\"}");

            yield return _publisher.PublishRoutine(Sketch());

            Assert.IsTrue(_publisher.LastResult.IsSuccess);
            Assert.IsTrue(_publisher.LastResult.IsDuplicate);
            StringAssert.Contains("duplicate", _publisher.LastResult.Message.ToLowerInvariant());
        }

        [UnityTest]
        public IEnumerator Publish_NewSketchAccepted_IsNotFlaggedAsDuplicate()
        {
            _transport.NextResponse = StubFieldLinkTransport.Status(201, "{\"id\":\"SK-1\"}");

            yield return _publisher.PublishRoutine(Sketch());

            Assert.IsFalse(_publisher.LastResult.IsDuplicate);
        }

        [UnityTest]
        public IEnumerator Publish_ServerRejects400_ReportsRejectedWithTheServersReason()
        {
            _transport.NextResponse = StubFieldLinkTransport.Status(400,
                "{\"error\":\"Sketch failed validation.\",\"failures\":[{\"field\":\"diameterInches\",\"message\":\"Diameter must be greater than zero.\"}]}");

            yield return _publisher.PublishRoutine(Sketch());

            Assert.AreEqual(PublishOutcome.RejectedByServer, _publisher.LastResult.Outcome);
            Assert.AreEqual(400, _publisher.LastResult.StatusCode);
            StringAssert.Contains("Diameter must be greater than zero.", _publisher.LastResult.Message);
        }

        [UnityTest]
        public IEnumerator Publish_ServerSays409_ReportsRejectedWithTheConflictMessage()
        {
            _transport.NextResponse = StubFieldLinkTransport.Status(409,
                "{\"error\":\"Sketch 'SK-1' was already published with different content.\"}");

            yield return _publisher.PublishRoutine(Sketch());

            Assert.AreEqual(PublishOutcome.RejectedByServer, _publisher.LastResult.Outcome);
            Assert.AreEqual(409, _publisher.LastResult.StatusCode);
            StringAssert.Contains("different content", _publisher.LastResult.Message);
        }

        private FieldSketchDto QueuedSketch()
        {
            var sketch = Sketch();
            _publisher.Queue.Enqueue(sketch);
            return sketch;
        }

        [UnityTest]
        public IEnumerator Publish_NoSignal_QueuesTheSketchAndSaysSo()
        {
            _transport.NextResponse = StubFieldLinkTransport.Offline();

            yield return _publisher.PublishRoutine(Sketch());

            Assert.AreEqual(PublishOutcome.QueuedOffline, _publisher.LastResult.Outcome);
            Assert.AreEqual(0, _publisher.LastResult.StatusCode);
            Assert.AreEqual(1, _publisher.Queue.Count);
            var reloaded = new PublishQueue(TestKey);
            reloaded.Load();
            Assert.AreEqual(1, reloaded.Count, "queue must survive an app restart");
        }

        [UnityTest]
        public IEnumerator Publish_ServerError5xx_QueuesForRetryAndSaysSo()
        {
            _transport.NextResponse = StubFieldLinkTransport.Status(503, "unavailable");

            yield return _publisher.PublishRoutine(Sketch());

            Assert.AreEqual(PublishOutcome.QueuedForRetry, _publisher.LastResult.Outcome);
            Assert.AreEqual(503, _publisher.LastResult.StatusCode);
            Assert.AreEqual(1, _publisher.Queue.Count);
            Assert.AreEqual(0, _publisher.Queue.Peek().retryCount);
        }

        [UnityTest]
        public IEnumerator Publish_ClientError4xx_IsNotQueued()
        {
            _transport.NextResponse = StubFieldLinkTransport.Status(400, "{\"error\":\"bad\"}");

            yield return _publisher.PublishRoutine(Sketch());

            Assert.AreEqual(PublishOutcome.RejectedByServer, _publisher.LastResult.Outcome);
            Assert.AreEqual(0, _publisher.Queue.Count);
        }

        [UnityTest]
        public IEnumerator Drain_On201_SendsTheQueuedSketchAndRemovesIt()
        {
            QueuedSketch();
            _transport.NextResponse = StubFieldLinkTransport.Status(201, "{}");

            yield return _publisher.DrainQueue();

            Assert.AreEqual(1, _transport.CallCount);
            Assert.AreEqual(0, _publisher.Queue.Count);
        }

        [UnityTest]
        public IEnumerator Drain_On200Duplicate_AlsoRemovesIt()
        {
            QueuedSketch();
            _transport.NextResponse = StubFieldLinkTransport.Status(200, "{}");

            yield return _publisher.DrainQueue();

            Assert.AreEqual(0, _publisher.Queue.Count);
            Assert.IsTrue(_publisher.LastResult.IsDuplicate);
        }

        [UnityTest]
        public IEnumerator Drain_StopsAtTheFirstNetworkFailure()
        {
            QueuedSketch();
            QueuedSketch();
            _transport.NextResponse = StubFieldLinkTransport.Offline();

            yield return _publisher.DrainQueue();

            Assert.AreEqual(1, _transport.CallCount);
            Assert.AreEqual(2, _publisher.Queue.Count);
        }

        [UnityTest]
        public IEnumerator Drain_On409_KeepsTheSketchQueuedButNeverResendsIt()
        {
            QueuedSketch();
            _transport.NextResponse = StubFieldLinkTransport.Status(409, "{\"error\":\"different content\"}");

            yield return _publisher.DrainQueue();
            yield return _publisher.DrainQueue();

            Assert.AreEqual(1, _transport.CallCount);
            Assert.AreEqual(1, _publisher.Queue.Count);
            Assert.IsFalse(string.IsNullOrEmpty(_publisher.Queue.Peek().holdReason));
        }

        [UnityTest]
        public IEnumerator Drain_5xxKeepsFailing_GivesUpAfterTheRetryCapAndHolds()
        {
            QueuedSketch();
            _transport.NextResponse = StubFieldLinkTransport.Status(503, "unavailable");

            for (var i = 0; i < PublishService.MaxRetries + 2; i++)
            {
                yield return _publisher.DrainQueue();
            }

            Assert.AreEqual(PublishService.MaxRetries, _transport.CallCount);
            Assert.AreEqual(1, _publisher.Queue.Count);
            Assert.IsFalse(string.IsNullOrEmpty(_publisher.Queue.Peek().holdReason));
        }

        [UnityTest]
        public IEnumerator Publish_Success_AlsoSendsWhatWasWaitingInTheQueue()
        {
            QueuedSketch();
            var fresh = Sketch();

            yield return _publisher.PublishRoutine(fresh);

            Assert.AreEqual(2, _transport.CallCount);
            Assert.AreEqual(0, _publisher.Queue.Count);
            StringAssert.Contains(fresh.id, _publisher.LastResult.Message);
        }

        // ---------------------------------------------------------------------
        // TODO(candidate): there is no test here for what happens when the tablet
        // is offline, when the server says 400, or when the same sketch is
        // published twice. That is not an oversight in the test file — the
        // behaviour does not exist yet. See PublishService.PublishRoutine.
        // ---------------------------------------------------------------------
    }
}
