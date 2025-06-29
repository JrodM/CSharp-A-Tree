using Microsoft.VisualStudio.TestTools.UnitTesting;
using ATree;

namespace ATree.Tests
{
    [TestClass]
    public class StringTableTests
    {
        [TestMethod]
        public void GetId_ForNewString_ReturnsNewId()
        {
            var stringTable = new StringTable();
            var id1 = stringTable.Intern("hello");
            var id2 = stringTable.Intern("world");

            Assert.AreNotEqual(id1, id2, "Different strings should have different IDs.");
        }

        [TestMethod]
        public void GetId_ForExistingString_ReturnsSameId()
        {
            var stringTable = new StringTable();
            var id1 = stringTable.Intern("hello");
            var id2 = stringTable.Intern("hello");

            Assert.AreEqual(id1, id2, "The same string should return the same ID.");
        }

        [TestMethod]
        public void GetString_ForValidId_ReturnsCorrectString()
        {
            var stringTable = new StringTable();
            var id = stringTable.Intern("hello");
            var value = stringTable.GetString(id);

            Assert.AreEqual("hello", value, "GetString should return the original string.");
        }

        [TestMethod]
        public void GetString_ForInvalidId_ThrowsException()
        {
            var stringTable = new StringTable();
            // Assuming IDs start from 0. An ID of 100 should be invalid for an empty table.
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => stringTable.GetString(100));
        }
    }
}
