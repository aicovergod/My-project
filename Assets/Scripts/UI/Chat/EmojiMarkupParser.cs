using System;
using System.Collections.Generic;
using System.Text;

namespace UI.Chat
{
    /// <summary>
    /// Converts chat markup (<c>&lt;emoji=##&gt;</c>, <c>&lt;ModIcon=##&gt;</c>) into a sequence of renderable tokens.
    /// </summary>
    public static class EmojiMarkupParser
    {
        private const string EmojiPrefix = "emoji";
        private const string ModIconPrefix = "modicon";

        /// <summary>
        /// Tokenises the supplied text into literal and emoji segments.
        /// </summary>
        /// <param name="text">Raw chat text potentially containing emoji markup.</param>
        /// <param name="emojiAtlas">Emoji atlas used for <c>&lt;emoji=...&gt;</c> sprite lookups. Defaults to <see cref="EmojiAtlas.Instance"/>.</param>
        /// <param name="modIconAtlas">Moderator icon atlas used for <c>&lt;ModIcon=...&gt;</c> sprite lookups. Defaults to <see cref="ModIconAtlas.Instance"/>.</param>
        /// <param name="allowModeratorIcons">
        /// When <c>true</c>, <c>&lt;ModIcon=...&gt;</c> markup will resolve into sprite tokens; otherwise the markup remains literal
        /// text so players cannot spoof moderator badges within unrestricted message fields.
        /// </param>
        /// <returns>List of tokens describing the message content.</returns>
        public static List<EmojiMarkupToken> Parse(string text, IEmojiAtlas emojiAtlas = null, IEmojiAtlas modIconAtlas = null, bool allowModeratorIcons = true)
        {
            var result = new List<EmojiMarkupToken>();

            if (text == null)
                return result;

            emojiAtlas ??= EmojiAtlas.Instance;
            if (allowModeratorIcons)
                modIconAtlas ??= ModIconAtlas.Instance;
            else
                modIconAtlas = null;
            var builder = new StringBuilder();

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '<' && TryParseSpriteTag(text, ref i, emojiAtlas, modIconAtlas, out var emojiToken))
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

        /// <summary>
        /// Attempts to parse a markup tag at the current index into an emoji token.
        /// </summary>
        /// <param name="source">Full markup string being processed.</param>
        /// <param name="index">Current parse index, which will be advanced when a token is produced.</param>
        /// <param name="emojiAtlas">Atlas used for standard emoji lookups.</param>
        /// <param name="modIconAtlas">Atlas used for moderator icon lookups.</param>
        /// <param name="token">Resolved emoji token when parsing succeeds.</param>
        /// <returns><c>true</c> when a recognised tag is converted into a sprite token.</returns>
        private static bool TryParseSpriteTag(string source, ref int index, IEmojiAtlas emojiAtlas, IEmojiAtlas modIconAtlas, out EmojiMarkupToken token)
        {
            token = default;
            int start = index;
            int closing = source.IndexOf('>', start + 1);
            if (closing == -1)
                return false;

            string contents = source.Substring(start + 1, closing - start - 1);
            if (!TryExtractTagMetadata(contents, out string prefix, out string key))
                return false;

            IEmojiAtlas atlas = ResolveAtlas(prefix, emojiAtlas, modIconAtlas);
            if (atlas != null && atlas.TryGetEmoji(key, out var definition))
            {
                token = EmojiMarkupToken.ForEmoji(definition);
                index = closing;
                return true;
            }

            // Unknown emoji – leave the markup untouched by re-appending it during the main loop.
            return false;
        }

        /// <summary>
        /// Resolves the appropriate atlas for the supplied markup prefix.
        /// </summary>
        /// <param name="prefix">Markup prefix extracted from the tag.</param>
        /// <param name="emojiAtlas">Atlas used for standard emoji markup.</param>
        /// <param name="modIconAtlas">Atlas used for moderator icon markup.</param>
        /// <returns>Atlas that should service the lookup, or <c>null</c> when the prefix is unknown.</returns>
        private static IEmojiAtlas ResolveAtlas(string prefix, IEmojiAtlas emojiAtlas, IEmojiAtlas modIconAtlas)
        {
            if (string.IsNullOrEmpty(prefix))
                return null;

            if (prefix.Equals(EmojiPrefix, StringComparison.OrdinalIgnoreCase))
                return emojiAtlas;

            if (prefix.Equals(ModIconPrefix, StringComparison.OrdinalIgnoreCase))
                return modIconAtlas;

            return null;
        }

        /// <summary>
        /// Extracts the markup prefix and key from a raw tag payload.
        /// </summary>
        /// <param name="contents">Inner text of the markup tag.</param>
        /// <param name="prefix">Prefix that determines which atlas to query.</param>
        /// <param name="key">Lookup key provided by the markup tag.</param>
        /// <returns><c>true</c> when the payload contains both prefix and key components.</returns>
        private static bool TryExtractTagMetadata(string contents, out string prefix, out string key)
        {
            prefix = string.Empty;
            key = string.Empty;
            if (string.IsNullOrWhiteSpace(contents))
                return false;

            int equalsIndex = contents.IndexOf('=');
            if (equalsIndex <= 0)
                return false;

            prefix = contents.Substring(0, equalsIndex).Trim();
            if (string.IsNullOrEmpty(prefix))
                return false;

            key = contents.Substring(equalsIndex + 1).Trim();
            return !string.IsNullOrEmpty(key);
        }
    }
}
