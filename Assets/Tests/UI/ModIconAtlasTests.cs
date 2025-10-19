using System.Reflection;
using NUnit.Framework;
using UI.Chat;

namespace Tests.UI
{
    public sealed class ModIconAtlasTests
    {
        [Test]
        public void ExtractKey_IgnoresUnitySpriteSuffix()
        {
            MethodInfo method = typeof(ModIconAtlas).GetMethod("ExtractKey", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "ExtractKey helper should exist");

            string key = (string)method.Invoke(null, new object[] { "ModIcon_04_0" });
            Assert.That(key, Is.EqualTo("04"));
        }

        [Test]
        public void ExtractKey_PreservesSingleUnderscoreKey()
        {
            MethodInfo method = typeof(ModIconAtlas).GetMethod("ExtractKey", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "ExtractKey helper should exist");

            string key = (string)method.Invoke(null, new object[] { "Icon_01" });
            Assert.That(key, Is.EqualTo("01"));
        }
    }
}
