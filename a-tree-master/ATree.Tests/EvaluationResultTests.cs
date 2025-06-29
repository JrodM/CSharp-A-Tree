using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using ATree;

namespace ATree.Tests
{
    [TestClass]
    public class EvaluationResultTests
    {
        private const int SizeLessThan64 = 15;
        private const int Size = 128;
        private const int AnId = 1;
        private const int AnIdThatExceeds64 = 67;

        [TestMethod]
        public void CanCreateWithASizeThatIsLessThan64()
        {
            var results = new EvaluationResult<string>(SizeLessThan64);
            results.SetResult(AnId, false);

            Assert.IsTrue(results.IsEvaluated(AnId));
            Assert.AreEqual(false, results.GetResult(AnId));
        }

        [TestMethod]
        public void WhenLookingIfUnevaluatedResultIsEvaluatedThenReturnFalse()
        {
            var results = new EvaluationResult<string>(Size);
            Assert.IsFalse(results.IsEvaluated(AnId));
        }

        [TestMethod]
        public void WhenLookingIfEvaluatedResultIsEvaluatedThenReturnTrue()
        {
            var results = new EvaluationResult<string>(Size);
            results.SetResult(AnId, false);
            Assert.IsTrue(results.IsEvaluated(AnId));
        }

        [TestMethod]
        public void CanSetASuccessfulResult()
        {
            var results = new EvaluationResult<string>(Size);
            results.SetResult(AnId, true);

            Assert.IsTrue(results.IsEvaluated(AnId));
            Assert.AreEqual(true, results.GetResult(AnId));
        }

        [TestMethod]
        public void CanSetAFailedResult()
        {
            var results = new EvaluationResult<string>(Size);
            results.SetResult(AnId, false);

            Assert.IsTrue(results.IsEvaluated(AnId));
            Assert.AreEqual(false, results.GetResult(AnId));
        }

        [TestMethod]
        public void CanSetAnUndefinedResult()
        {
            var results = new EvaluationResult<string>(Size);
            results.SetResult(AnId, null);

            Assert.IsTrue(results.IsEvaluated(AnId));
            Assert.IsNull(results.GetResult(AnId));
        }

        [TestMethod]
        public void CanSetIdThatExceeds64()
        {
            var results = new EvaluationResult<string>(Size);
            results.SetResult(AnIdThatExceeds64, false);

            Assert.IsTrue(results.IsEvaluated(AnIdThatExceeds64));
            Assert.AreEqual(false, results.GetResult(AnIdThatExceeds64));
        }
    }
}
