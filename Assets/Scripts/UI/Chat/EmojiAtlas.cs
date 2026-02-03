using System;
using System.Collections.Generic;
using UnityEngine;

namespace UI.Chat
{
    /// <summary>
    /// Runtime emoji catalog that lazily loads sprites from <c>Resources/Sprites/Emojis</c>.
    /// </summary>
    public sealed class EmojiAtlas : IEmojiAtlas
    {
        private const string ResourcePath = "Sprites/Emojis";

        private static readonly Lazy<EmojiAtlas> LazyInstance = new Lazy<EmojiAtlas>(() => new EmojiAtlas());

        private readonly Dictionary<string, EmojiSpriteDefinition> lookup;
        private readonly List<EmojiSpriteDefinition> orderedDefinitions;

        private EmojiAtlas()
        {
            lookup = new Dictionary<string, EmojiSpriteDefinition>(StringComparer.OrdinalIgnoreCase);
            orderedDefinitions = new List<EmojiSpriteDefinition>();
            LoadSprites();
        }

        /// <summary>Singleton instance used throughout the UI layer.</summary>
        public static EmojiAtlas Instance => LazyInstance.Value;

        /// <inheritdoc />
        public bool TryGetEmoji(string key, out EmojiSpriteDefinition definition)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                definition = default;
                return false;
            }

            string normalized = NormalizeKey(key);
            if (lookup.TryGetValue(normalized, out definition))
                return true;

            return lookup.TryGetValue(key.Trim(), out definition);
        }

        /// <inheritdoc />
        public IReadOnlyList<EmojiSpriteDefinition> GetAllEmojis() => orderedDefinitions;

        private void LoadSprites()
        {
            lookup.Clear();
            orderedDefinitions.Clear();

            Sprite[] sprites = Resources.LoadAll<Sprite>(ResourcePath);
            Array.Sort(sprites, (a, b) => string.CompareOrdinal(a?.name, b?.name));

            foreach (var sprite in sprites)
            {
                if (sprite == null)
                    continue;

                string key = ExtractKey(sprite.name);
                if (string.IsNullOrEmpty(key))
                    continue;

                key = NormalizeKey(key);
                var definition = new EmojiSpriteDefinition(key, sprite);
                lookup[key] = definition;
                orderedDefinitions.Add(definition);

                if (int.TryParse(key, out int numeric))
                {
                    string numericKey = numeric.ToString();
                    lookup[numericKey] = definition;
                }
            }
        }

        private static string ExtractKey(string spriteName)
        {
            if (string.IsNullOrEmpty(spriteName))
                return string.Empty;

            string trimmed = spriteName.Trim();
            string[] segments = trimmed.Split('_');
            if (segments.Length >= 2)
            {
                for (int i = 1; i < segments.Length; i++)
                {
                    string segment = segments[i];
                    if (string.IsNullOrWhiteSpace(segment))
                        continue;

                    // Prefer the first non-empty segment after the prefix, which holds the emoji index
                    // for names like "Emoji_01_0". Keeping this logic ensures atlas ordering remains
                    // consistent regardless of variant suffixes.
                    if (segments.Length == 2 || ContainsDigit(segment))
                        return segment;
                }
            }

            int underscore = trimmed.LastIndexOf('_');
            if (underscore >= 0 && underscore < trimmed.Length - 1)
                trimmed = trimmed.Substring(underscore + 1);

            if (trimmed.StartsWith("Emoji", StringComparison.OrdinalIgnoreCase) && trimmed.Length > 5)
                trimmed = trimmed.Substring(5);

            return trimmed;
        }

        private static bool ContainsDigit(string value)
        {
            foreach (char character in value)
            {
                if (char.IsDigit(character))
                    return true;
            }

            return false;
        }

        private static string NormalizeKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return string.Empty;

            key = key.Trim();
            if (int.TryParse(key, out int numeric))
                return numeric.ToString("00");

            return key;
        }
    }
}
