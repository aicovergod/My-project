using System.Collections.Generic;

namespace UI.Chat
{
    /// <summary>
    /// Abstraction used by the emoji parsing and rendering helpers to resolve sprites without tightly coupling to the runtime service.
    /// </summary>
    public interface IEmojiAtlas
    {
        /// <summary>
        /// Attempts to resolve an emoji sprite by key.
        /// </summary>
        /// <param name="key">Lookup key supplied by the markup tag.</param>
        /// <param name="definition">Resolved sprite definition.</param>
        /// <returns><c>true</c> when the sprite exists, otherwise <c>false</c>.</returns>
        bool TryGetEmoji(string key, out EmojiSpriteDefinition definition);

        /// <summary>
        /// Returns every emoji currently registered with the atlas.
        /// </summary>
        IReadOnlyList<EmojiSpriteDefinition> GetAllEmojis();
    }
}
