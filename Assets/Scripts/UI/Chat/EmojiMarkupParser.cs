using System;
using System.Collections.Generic;
using System.Text;

namespace UI.Chat
{
    /// <summary>
    /// Converts chat markup (<c>&lt;emoji=##&gt;</c>) into a sequence of renderable tokens.
    /// </summary>
    public static class EmojiMarkupParser
    {
        private const string EmojiPrefix = "emoji";

        /// <summary>
        /// Tokenises the supplied text into literal and emoji segments.
        /// </summary>
        /// <param name="text">Raw chat text potentially containing emoji markup.</param>
        /// <param name="atlas">Emoji atlas used for sprite lookups. Defaults to <see cref="EmojiAtlas.Instance"/>.</param>
        /// <returns>List of tokens describing the message content.</returns>
        public static List<EmojiMarkupToken> Parse(string text, IEmojiAtlas atlas = null)
        {
            var result = new List<EmojiMarkupToken>();

            if (text == null)
                return result;

            atlas ??= EmojiAtlas.Instance;
            var builder = new StringBuilder();

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '<' && TryParseEmojiTag(text, ref i, atlas, out var emojiToken))
                {
                    if (builder.Length > 0)
                    {
                        result.Add(EmojiMarkupToken.ForText(builder.ToString()));
                        builder.Length = 0;
                    }

                    if (emojiToken.IsEmoji)
                        result.Add(emojiToken);

                    continue;
                }

                builder.Append(c);
            }

            if (builder.Length > 0)
                result.Add(EmojiMarkupToken.ForText(builder.ToString()));

            if (result.Count == 0)
                result.Add(EmojiMarkupToken.ForText(string.Empty));

            return result;
        }

        private static bool TryParseEmojiTag(string source, ref int index, IEmojiAtlas atlas, out EmojiMarkupToken token)
        {
            token = default;
            int start = index;
            int closing = source.IndexOf('>', start + 1);
            if (closing == -1)
                return false;

            string contents = source.Substring(start + 1, closing - start - 1);
            if (!TryExtractEmojiKey(contents, out string key))
                return false;

            if (atlas != null && atlas.TryGetEmoji(key, out var definition))
            {
                token = EmojiMarkupToken.ForEmoji(definition);
                index = closing;
                return true;
            }

            // Unknown emoji – leave the markup untouched by re-appending it during the main loop.
            return false;
        }

        private static bool TryExtractEmojiKey(string contents, out string key)
        {
            key = string.Empty;
            if (string.IsNullOrWhiteSpace(contents))
                return false;

            int equalsIndex = contents.IndexOf('=');
            if (equalsIndex <= 0)
                return false;

            string prefix = contents.Substring(0, equalsIndex).Trim();
            if (!prefix.Equals(EmojiPrefix, StringComparison.OrdinalIgnoreCase))
                return false;

            key = contents.Substring(equalsIndex + 1).Trim();
            return !string.IsNullOrEmpty(key);
        }
    }
}
