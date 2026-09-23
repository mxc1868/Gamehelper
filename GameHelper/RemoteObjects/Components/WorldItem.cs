// <copyright file="WorldItem.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.RemoteObjects.Components
{
    using System;
    using System.Diagnostics.CodeAnalysis;
    using GameHelper.RemoteObjects.States.InGameStateObjects;
    using GameHelper.Utils;
    using GameOffsets.Objects.Components;
    using ImGuiNET;

    /// <summary>
    ///     The <see cref="WorldItem" /> component — present on a dropped/ground item entity. The ground
    ///     entity is a wrapper; the real item (carrying <see cref="Mods" />, <see cref="Base" />,
    ///     <see cref="RenderItem" />, <see cref="Stack" />) lives at <see cref="ItemEntityAddress" />.
    /// </summary>
    public class WorldItem : ComponentBase
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="WorldItem" /> class.
        /// </summary>
        /// <param name="address">address of the <see cref="WorldItem" /> component.</param>
        public WorldItem(IntPtr address)
            : base(address) { }

        /// <summary>
        ///     Gets the address of the inner item entity (the one carrying the item components).
        ///     <see cref="IntPtr.Zero" /> when unavailable.
        /// </summary>
        public IntPtr ItemEntityAddress { get; private set; } = IntPtr.Zero;

        /// <summary>
        ///     Reads a fresh inner item without caching it on the ground entity.
        ///     This fork API returns false for an unreadable or changing item pointer.
        /// </summary>
        public bool TryReadItem([NotNullWhen(true)] out Item? item)
        {
            item = null;
            var reader = Core.Process.Handle;
            if (!reader.TryReadMemory<WorldItemOffsets>(this.Address, out var before) ||
                before.Header.EntityPtr == IntPtr.Zero || before.Header.EntityPtr != this.OwnerEntityAddress ||
                !SafeMemoryHandle.IsValidAddress(before.ItemEntityPtr)) return false;

            var candidate = new Item(before.ItemEntityPtr);
            if (!candidate.IsValid || !candidate.Path.StartsWith("Metadata/Items/", StringComparison.Ordinal) ||
                !reader.TryReadMemory<WorldItemOffsets>(this.Address, out var after) ||
                before.Header.EntityPtr != after.Header.EntityPtr || before.ItemEntityPtr != after.ItemEntityPtr) return false;

            item = candidate;
            return true;
        }

        /// <summary>
        ///     Converts the <see cref="WorldItem" /> class data to ImGui.
        /// </summary>
        internal override void ToImGui()
        {
            base.ToImGui();
            ImGui.Text($"Item Entity: {this.ItemEntityAddress.ToInt64():X}");
        }

        /// <inheritdoc />
        protected override void UpdateData(bool hasAddressChanged)
        {
            var reader = Core.Process.Handle;
            var data = reader.ReadMemory<WorldItemOffsets>(this.Address);
            this.OwnerEntityAddress = data.Header.EntityPtr;
            this.ItemEntityAddress = data.ItemEntityPtr;
        }
    }
}
