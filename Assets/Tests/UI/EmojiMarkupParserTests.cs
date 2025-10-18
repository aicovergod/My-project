using System.Collections.Generic;
using NUnit.Framework;
using UI.Chat;

namespace Tests.UI
{
    public sealed class EmojiMarkupParserTests
    {
        [Test]
        public void Parse_KnownEmojiProducesSeparateToken()
        {
            var atlas = new StubAtlas("01");
            var tokens = EmojiMarkupParser.Parse("Hi <emoji=01> there", atlas);

            Assert.That(tokens, Is.Not.Null);
            Assert.That(tokens.Count, Is.EqualTo(3));
            Assert.That(tokens[0].IsText, Is.True);
            Assert.That(tokens[0].Text, Is.EqualTo("Hi "));
            Assert.That(tokens[1].IsEmoji, Is.True);
            Assert.That(tokens[1].Emoji.Key, Is.EqualTo("01"));
            Assert.That(tokens[2].IsText, Is.True);
            Assert.That(tokens[2].Text, Is.EqualTo(" there"));
        }

        [Test]
        public void Parse_UnknownEmojiFallsBackToLiteral()
        {
            var atlas = new StubAtlas();
            const string message = "Missing <emoji=99> tag";
            var tokens = EmojiMarkupParser.Parse(message, atlas);

            Assert.That(tokens, Is.Not.Null);
            Assert.That(tokens.Count, Is.EqualTo(1));
            Assert.That(tokens[0].IsText, Is.True);
            Assert.That(tokens[0].Text, Is.EqualTo(message));
        }

        private sealed class StubAtlas : IEmojiAtlas
        {
            private readonly Dictionary<string, EmojiSpriteDefinition> entries = new Dictionary<string, EmojiSpriteDefinition>();

            public StubAtlas(params string[] keys)
            {
                if (keys == null)
                    return;

                for (int i = 0; i < keys.Length; i++)
                {
                    if (string.IsNullOrEmpty(keys[i]))
                        continue;

                    string normalised = keys[i];
                    entries[normalised] = new EmojiSpriteDefinition(normalised, null);
                }
            }

            public IReadOnlyList<EmojiSpriteDefinition> GetAllEmojis() => new List<EmojiSpriteDefinition>(entries.Values);

            public bool TryGetEmoji(string key, out EmojiSpriteDefinition definition) => entries.TryGetValue(key, out definition);
        }
    }
}
