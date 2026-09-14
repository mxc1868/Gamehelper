// <copyright file="Helper.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace Radar
{
    using System;
    using System.Numerics;

    /// <summary>
    /// Contains the helper functions.
    /// </summary>
    public static class Helper
    {
        /// <summary>
        /// Map rotation in Radian format.
        /// </summary>
        public static readonly double CameraAngle = GameHelper.Utils.MapProjection.CameraAngle;
        private static double diagonalLength = 0x00;
        private static float scale = 0.5f;
        private static GameHelper.Utils.MapProjection projection;

        /// <summary>
        /// Sets the diagonal length of the Mini/Large Map UiElement,
        /// depending on what's visible.
        /// </summary>
        public static double DiagonalLength
        {
            private get
            {
                return diagonalLength;
            }

            set
            {
                if (value > 0 && value != diagonalLength)
                {
                    diagonalLength = value;
                    UpdateCosSin();
                }
            }
        }

        /// <summary>
        /// Sets the scale of the Mini/Large Map, depending on what's visible.
        /// </summary>
        public static float Scale
        {
            private get
            {
                return scale;
            }

            set
            {
                if (value > 0 && value != scale)
                {
                    scale = value;
                    UpdateCosSin();
                }
            }
        }

        /// <summary>
        /// Converts Entity to Player delta w.r.t Grid Postion
        /// to the Minimap pixel location Delta.
        /// </summary>
        /// <param name="delta">
        /// Grid postion delta between player and the entity to draw.
        /// This is due to the fact that player always remains at center of the mini/large,
        /// if we ignore the map shifting feature.
        /// </param>
        /// <param name="deltaZ">
        /// Terrain level difference between player and entity.
        /// </param>
        /// <returns>nothing.</returns>
        public static Vector2 DeltaInWorldToMapDelta(Vector2 delta, float deltaZ)
        {
            return projection.ProjectDelta(delta, deltaZ);
        }

        private static void UpdateCosSin()
        {
            projection = new GameHelper.Utils.MapProjection(DiagonalLength, Scale);
        }
    }
}
