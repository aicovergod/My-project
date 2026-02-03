using UnityEngine;

namespace UI.Chat
{
    /// <summary>
    /// Token produced by <see cref="EmojiMarkupParser"/> describing either a literal text span or an emoji sprite.
    /// </summary>
    public readonly struct EmojiMarkupToken
    {
        /// <summary>
        /// Enumerates the supported token types.
        /// </summary>
        public enum TokenType
        {
            /// <summary>Plain text payload that should be rendered using a <see cref="UnityEngine.UI.Text"/> component.</summary>
            Text,

            /// <summary>Emoji sprite resolved through <see cref="EmojiAtlas"/>.</summary>
            Emoji
        }

        private EmojiMarkupToken(TokenType type, string text, EmojiSpriteDefinition emoji)
        {
            Type = type;
            Text = text;
            Emoji = emoji;
        }

        /// <summary>
        /// Creates a literal text token.
        /// </summary>
        /// <param name="text">Text payload to render verbatim.</param>
        /// <returns>Token instance representing the supplied text.</returns>
        public static EmojiMarkupToken ForText(string text) => new EmojiMarkupToken(TokenType.Text, text, default);

        /// <summary>
        /// Creates an emoji sprite token.
        /// </summary>
        /// <param name="definition">Emoji sprite definition resolved from the atlas.</param>
        /// <returns>Token instance representing the supplied emoji.</returns>
        public static EmojiMarkupToken ForEmoji(EmojiSpriteDefinition definition) => new EmojiMarkupToken(TokenType.Emoji, null, definition);

        /// <summary>Type of data stored within this token.</summary>
        public TokenType Type { get; }

        /// <summary>Literal text payload. Only populated when <see cref="Type"/> is <see cref="TokenType.Text"/>.</summary>
        public string Text { get; }

        /// <summary>Emoji sprite definition. Only populated when <see cref="Type"/> is <see cref="TokenType.Emoji"/>.</summary>
        public EmojiSpriteDefinition Emoji { get; }

        /// <summary>Indicates whether this token represents an emoji sprite.</summary>
        public bool IsEmoji => Type == TokenType.Emoji;

        /// <summary>Indicates whether this token represents a literal text span.</summary>
        public bool IsText => Type == TokenType.Text;
    }
}
