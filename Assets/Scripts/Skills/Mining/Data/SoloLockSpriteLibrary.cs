using System;
using UnityEngine;

namespace Skills.Mining
{
    /// <summary>
    ///     Provides runtime access to the solo-lock sprite used by personal ore nodes. The sprite is generated from an
    ///     embedded 64x64 PNG so we can avoid committing binary art assets while still supplying a consistent icon.
    /// </summary>
    public static class SoloLockSpriteLibrary
    {
        private static Sprite cachedSprite;

        /// <summary>
        ///     Shared solo-lock sprite instance. The texture is generated on demand and cached for reuse by all nodes.
        /// </summary>
        public static Sprite DefaultSprite
        {
            get
            {
                if (cachedSprite == null)
                    cachedSprite = CreateSprite();

                return cachedSprite;
            }
        }

        private static Sprite CreateSprite()
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                name = "SoloLockTexture"
            };

            var imageData = Convert.FromBase64String(EncodedSpriteData);
            ImageConversion.LoadImage(texture, imageData, markNonReadable: false);

            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;

            var sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                64f);
            sprite.name = "SoloLockSprite";

            return sprite;
        }

        /// <summary>
        ///     Base64 encoded PNG representing a 64x64 transparent lock icon.
        /// </summary>
        private const string EncodedSpriteData =
            "iVBORw0KGgoAAAANSUhEUgAAAEAAAABACAYAAACqaXHeAAABvklEQVR4nO2ZvXHDMAyFwVyGUKtalVR6ghQawkOkTZU2Q2iIFJrAZVKldpstlMo5HU1SP3wEctH7Kp9p"
            + "E8AjAJKSCCGEEEIIOSbOwujH+9MUG+v6UdUnNWOpoGNoiFHcwJ7AfUoK8VBqYhFM8Mh5QhRRdsnhtqmiY59f38m50dkAFyAWfNePbrqeV6+kqweXmmuvfz5FS+BG21Sy"
            + "JXgRkel6nlKZggIqQGjFcoMI/R/ZE2CptDV4Vw93tlNZEuoNiFJ4zJ0gRiz4UOD+WEiItqkWG+QeICXgr35oZVw9uFTwa37rz4soBYgAvmNbG95a5vN2/egQJVDkHOAL"
            + "sHblS89lQm42lMomQggRKbALaDQt5E4AvQtodWykHZgA2tsVyh5EAKu9GmE3WwDrg0qufZUHIn+ZrG7q38Y0nuDM8a/Hey5Hh8+AYg9E1vD8cvn9/PZ6MvHBRIB54P53"
            + "2kIcvgTUBQit/pZxNMwAawesURdgqclpN8GsXWDr+z40t4NXzvXYpARiq2xxFmAPsHbAGgpg7YA1FMDaAWuyBbB+WZlrH5IBViIg7EId1zwVokSH9gCtTLAuO0IIIYSQ"
            + "f8EPIEGtB8PKbaAAAAAASUVORK5CYII=";
    }
}
