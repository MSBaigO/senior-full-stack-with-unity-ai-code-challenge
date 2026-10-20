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

        [SetUp]
        public void SetUp()
        {
            _host = new GameObject("PublishServiceHost");
            _publisher = _host.AddComponent<PublishService>();
            _transport = new StubFieldLinkTransport();
            _publisher.Transport = _transport;
            _publisher.Queue.Clear();
        }

        [TearDown]
        public void TearDown()
        {
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

        // ---------------------------------------------------------------------
        // TODO(candidate): there is no test here for what happens when the tablet
        // is offline, when the server says 400, or when the same sketch is
        // published twice. That is not an oversight in the test file — the
        // behaviour does not exist yet. See PublishService.PublishRoutine.
        // ---------------------------------------------------------------------
    }
}
