namespace GameHelper.Utils
{
    using System;
    using System.Numerics;

    /// <summary>Independent map projection coefficients; safe to use in multiple plugins.</summary>
    public readonly struct MapProjection
    {
        public const double CameraAngle = 38.7 * Math.PI / 180;
        private readonly float cos;
        private readonly float sin;

        public MapProjection(double diagonalLength, float scale)
        {
            this.IsValid = double.IsFinite(diagonalLength) && diagonalLength > 0 &&
                float.IsFinite(scale) && scale > 0;
            var mapScale = 240f / scale;
            this.cos = this.IsValid ? (float)(diagonalLength * Math.Cos(CameraAngle) / mapScale) : 0;
            this.sin = this.IsValid ? (float)(diagonalLength * Math.Sin(CameraAngle) / mapScale) : 0;
        }

        public bool IsValid { get; }

        /// <param name="delta">Entity minus map origin, in grid units.</param>
        /// <param name="deltaZ">Entity terrain height minus origin terrain height, in world units.</param>
        public Vector2 ProjectDelta(Vector2 delta, float deltaZ) =>
            new((delta.X - delta.Y) * this.cos,
                ((deltaZ / 10.86957f) - (delta.X + delta.Y)) * this.sin);
    }
}
