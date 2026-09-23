namespace WhereTheWispsAt
{
    using System;
    using System.Numerics;
    using GameHelper.Plugin;

    public sealed class WhereTheWispsAtSettings : IPSettings
    {
        public bool DrawMap = true;
        public bool DrawMiniMap;
        public bool DrawMapLines = true;
        public bool DrawGround;
        public bool DrawChestGround = true;
        public bool DrawLabels = true;
        public bool ShowUnknownResources = true;
        public bool HideWhenUnfocused = true;
        public bool HideWhenPanelsOpen = true;
        public bool FollowCoopCenter;
        public int ScanIntervalMs = 500;
        public float MarkerSize = 5;
        public float LineWidth = 2;
        public float MaxLinkGridDistance = 30;
        public float GroundWidth = 30;
        public float GroundHeight = 10;
        public float GroundOpacity = 0.4f;
        public float ChestDistance = 100;
        public float MapScaleMultiplier = 1;
        public float MiniMapScaleMultiplier = 1;
        public Vector2 MapOffset;
        public Vector2 MiniMapOffset;
        public Vector4 Blue = new(0.35f, 0.75f, 1, 1);
        public Vector4 Yellow = new(1, 0.9f, 0.1f, 1);
        public Vector4 Purple = new(0.75f, 0.25f, 1, 1);
        public Vector4 Sacred = new(1, 0.5f, 0, 1);
        public int SacredColorVersion;
        public Vector4 Chest = Vector4.One;

        public void Normalize()
        {
            if (this.SacredColorVersion < 1)
            {
                // Upgrade the old white default once while retaining other custom colors.
                if (this.Sacred == Vector4.One) this.Sacred = new(1, 0.5f, 0, 1);
                this.SacredColorVersion = 1;
            }
            this.ScanIntervalMs = Math.Clamp(this.ScanIntervalMs, 100, 5000);
            this.MarkerSize = Clamp(this.MarkerSize, 1, 30, 5);
            this.LineWidth = Clamp(this.LineWidth, 0.5f, 10, 2);
            this.MaxLinkGridDistance = Clamp(this.MaxLinkGridDistance, 1, 100, 30);
            this.GroundWidth = Clamp(this.GroundWidth, 1, 200, 30);
            this.GroundHeight = Clamp(this.GroundHeight, 1, 200, 10);
            this.GroundOpacity = Clamp(this.GroundOpacity, 0, 1, 0.4f);
            this.ChestDistance = Clamp(this.ChestDistance, 1, 500, 100);
            this.MapScaleMultiplier = Clamp(this.MapScaleMultiplier, 0.1f, 3, 1);
            this.MiniMapScaleMultiplier = Clamp(this.MiniMapScaleMultiplier, 0.1f, 3, 1);
            this.MapOffset = new(Clamp(this.MapOffset.X, -2000, 2000, 0), Clamp(this.MapOffset.Y, -2000, 2000, 0));
            this.MiniMapOffset = new(Clamp(this.MiniMapOffset.X, -2000, 2000, 0), Clamp(this.MiniMapOffset.Y, -2000, 2000, 0));
            this.Blue = Color(this.Blue);
            this.Yellow = Color(this.Yellow);
            this.Purple = Color(this.Purple);
            this.Sacred = Color(this.Sacred);
            this.Chest = Color(this.Chest);
        }

        private static float Clamp(float value, float min, float max, float fallback) =>
            float.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;

        private static Vector4 Color(Vector4 v) => new(
            Clamp(v.X, 0, 1, 1), Clamp(v.Y, 0, 1, 1), Clamp(v.Z, 0, 1, 1), Clamp(v.W, 0, 1, 1));
    }
}
