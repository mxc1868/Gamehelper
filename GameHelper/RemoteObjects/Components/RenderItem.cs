// <copyright file="RenderItem.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.RemoteObjects.Components
{
    using System;
    using GameOffsets.Objects.Components;
    using ImGuiNET;

    /// <summary>
    ///     The <see cref="RenderItem" /> component in the entity. Present on item entities, it
    ///     exposes the item's 2D inventory art (.dds) path.
    /// </summary>
    /// <remarks>
    ///     The full art path can be matched against an external unique-item catalog without
    ///     reading identified modifiers. Multiple items may share art; callers must preserve
    ///     all candidates. A basename alone is not a guaranteed unique identity.
    /// </remarks>
    public class RenderItem : ComponentBase
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="RenderItem" /> class.
        /// </summary>
        /// <param name="address">address of the <see cref="RenderItem" /> component.</param>
        public RenderItem(IntPtr address)
            : base(address) { }

        /// <summary>
        ///     Gets the full 2D art (.dds) resource path of the item's own icon. Empty when unavailable.
        /// </summary>
        public string ResourcePath { get; private set; } = string.Empty;

        /// <summary>
        ///     Converts the <see cref="RenderItem" /> class data to ImGui.
        /// </summary>
        internal override void ToImGui()
        {
            base.ToImGui();
            ImGui.Text($"Resource Path: {this.ResourcePath}");
        }

        /// <inheritdoc />
        protected override void UpdateData(bool hasAddressChanged)
        {
            var reader = Core.Process.Handle;
            var data = reader.ReadMemory<RenderItemOffsets>(this.Address);
            this.OwnerEntityAddress = data.Header.EntityPtr;

            this.ResourcePath = data.ResourcePathPtr != IntPtr.Zero
                ? reader.ReadUnicodeString(data.ResourcePathPtr)
                : string.Empty;
        }
    }
}
