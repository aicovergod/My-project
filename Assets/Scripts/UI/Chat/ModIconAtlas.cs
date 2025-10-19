using System;
using System.Collections.Generic;
using UnityEngine;

namespace UI.Chat
{
    /// <summary>
    /// Runtime atlas that resolves moderator icon sprites from <c>Resources/Sprites/ModIcons</c>.
    /// </summary>
    public sealed class ModIconAtlas : IEmojiAtlas
    {
        private const string ResourcePath = "Sprites/ModIcons";

        private static readonly Lazy<ModIconAtlas> LazyInstance = new Lazy<ModIconAtlas>(() => new ModIconAtlas());

        private readonly Dictionary<string, EmojiSpriteDefinition> lookup;
        private readonly List<EmojiSpriteDefinition> orderedDefinitions;

        private ModIconAtlas()
        {
            lookup = new Dictionary<string, EmojiSpriteDefinition>(StringComparer.OrdinalIgnoreCase);
            orderedDefinitions = new List<EmojiSpriteDefinition>();
            LoadSprites();
        }

        /// <summary>
        /// Singleton instance used by chat components to resolve moderator icons.
        /// </summary>
        public static ModIconAtlas Instance => LazyInstance.Value;

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

        /// <summary>
        /// Populates the in-memory lookup by loading sprites from the Resources directory.
        /// </summary>
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

        /// <summary>
        /// Extracts the lookup key from the supplied sprite name.
        /// </summary>
        private static string ExtractKey(string spriteName)
        {
            if (string.IsNullOrEmpty(spriteName))
                return string.Empty;

            string trimmed = spriteName.Trim();
            int underscore = trimmed.LastIndexOf('_');
            if (underscore >= 0 && underscore < trimmed.Length - 1)
                return trimmed.Substring(underscore + 1);

            return trimmed;
        }

        /// <summary>
        /// Normalises the supplied key to ensure numeric identifiers are zero padded.
        /// </summary>
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
