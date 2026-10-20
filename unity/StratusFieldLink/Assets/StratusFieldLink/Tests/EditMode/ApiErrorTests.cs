using NUnit.Framework;
using StratusFieldLink.Model;

namespace StratusFieldLink.Tests.EditMode
{
    public class ApiErrorTests
    {
        [Test]
        public void Describe_SimpleError_ReturnsTheServerMessage()
        {
            Assert.AreEqual("Nope.", ApiErrorDto.Describe("{\"error\":\"Nope.\"}"));
        }

        [Test]
        public void Describe_ValidationFailures_ListsEachFieldMessage()
        {
            var body = "{\"error\":\"Sketch failed validation.\",\"failures\":[" +
                       "{\"field\":\"nodes\",\"message\":\"A run needs at least two nodes.\"}," +
                       "{\"field\":\"crewId\",\"message\":\"Every sketch is attributed to a crew.\"}]}";

            var text = ApiErrorDto.Describe(body);

            StringAssert.Contains("A run needs at least two nodes.", text);
            StringAssert.Contains("Every sketch is attributed to a crew.", text);
        }

        [TestCase("")]
        [TestCase(null)]
        [TestCase("<html>502 Bad Gateway</html>")]
        public void Describe_NothingUsable_ReturnsEmpty(string body)
        {
            Assert.AreEqual(string.Empty, ApiErrorDto.Describe(body));
        }
    }
}
