// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the MIT license.

using System.Linq;
using MTConnect.Devices;
using MTConnect.Devices.DataItems;
using NUnit.Framework;

namespace MTConnect.NET_Common_Tests.Devices
{
    /// <summary>
    /// Direct unit coverage FLOOR for <see cref="Device.RemoveComposition(string)"/>
    /// and <see cref="Device.RemoveDataItem(string)"/> — the two overrides
    /// PR #219 commit be42f52b made recursive / top-level-aware to close
    /// F-TEST-BUG-1 (nested Composition unremovable) and F-TEST-BUG-2
    /// (top-level Device DataItem unremovable). The existing
    /// <c>DeviceValidationLevelEnumArmTests</c> exercises the fix through
    /// <c>MTConnect.Agents.MTConnectAgent.NormalizeDevice</c>; this
    /// fixture pins the SAME methods against the coverage-FLOOR shapes
    /// the audit brief for cycle-2 explicitly listed — top-level
    /// Device.Compositions collection, great-grandchild Component depth,
    /// idempotent no-op on missing ID, and empty-tree safety.
    ///
    /// A regression that reverts either method to its pre-fix shape
    /// (skipping the top-level collection, or dropping the recursion)
    /// fails these tests before the higher-level NormalizeDevice fixture
    /// can flag it — smaller failing surface, faster diagnosis.
    /// </summary>
    [TestFixture]
    [Category("DeviceRemoveRecursion")]
    public class DeviceRemoveRecursionTests
    {
        // --------------------------------------------------------------
        // RemoveComposition — every code path in the recursive override.
        // --------------------------------------------------------------

        /// <summary>
        /// Pins that a Composition placed directly on <c>Device.Compositions</c>
        /// (the collection the base <see cref="Component"/> exposes on every
        /// Device / Component / Composition holder) is dropped by
        /// <see cref="Device.RemoveComposition"/>. The recursive override
        /// added by be42f52b handles this via the leading top-level branch
        /// before descending; that branch would silently skip if the
        /// implementation ever regressed to component-only recursion.
        /// </summary>
        [Test]
        public void RemoveComposition_drops_top_level_Composition_directly_on_Device()
        {
            var device = new Device { Id = "d1", Uuid = "d1", Name = "d1", Type = Device.TypeId };
            device.AddComposition(new Composition { Id = "top-comp", Name = "top-comp", Type = "Generic" });

            Assert.That(device.Compositions.Any(c => c.Id == "top-comp"), Is.True,
                "precondition — top-level Composition must be present before removal.");

            device.RemoveComposition("top-comp");

            Assert.That(device.Compositions == null || !device.Compositions.Any(c => c.Id == "top-comp"), Is.True,
                "Device.RemoveComposition must drop a Composition placed directly on Device.Compositions.");
        }

        /// <summary>
        /// Pins great-grandchild-depth Composition removal:
        /// Device → Component → subComponent → subsubComponent → Composition.
        /// The recursive <c>RemoveComposition(IComponent, string)</c> helper
        /// descends via <c>component.Components</c>; a regression to a
        /// single-level walk would fail this at depth 3.
        /// </summary>
        [Test]
        public void RemoveComposition_recurses_into_great_grandchild_Component()
        {
            var device = new Device { Id = "d1", Uuid = "d1", Name = "d1", Type = Device.TypeId };

            var l1 = new Component { Id = "l1", Name = "l1", Type = "Axes" };
            var l2 = new Component { Id = "l2", Name = "l2", Type = "Linear" };
            var l3 = new Component { Id = "l3", Name = "l3", Type = "Motor" };
            l3.AddComposition(new Composition { Id = "deep-comp", Name = "deep-comp", Type = "Generic" });
            l2.AddComponent(l3);
            l1.AddComponent(l2);
            device.AddComponent(l1);

            Assert.That(device.GetCompositions().Any(c => c.Id == "deep-comp"), Is.True,
                "precondition — great-grandchild Composition must be reachable via GetCompositions before removal.");

            device.RemoveComposition("deep-comp");

            Assert.That(device.GetCompositions() == null || !device.GetCompositions().Any(c => c.Id == "deep-comp"), Is.True,
                "Device.RemoveComposition must recurse through Component→subComponent→subsubComponent to drop a deeply nested Composition — matches the shape of RemoveComponent recursion the fix mirrors.");
        }

        /// <summary>
        /// Pins idempotency: calling <see cref="Device.RemoveComposition"/>
        /// with an ID that does not exist anywhere in the tree is a
        /// silent no-op — no throw, no mutation of the surviving
        /// Compositions collection. The recursive override must not
        /// throw on missing IDs because <c>NormalizeDevice</c> calls it
        /// per-invalid-Composition and any exception would abort device
        /// onboarding for the remaining valid children.
        /// </summary>
        [Test]
        public void RemoveComposition_nonexistent_id_is_idempotent_no_op()
        {
            var device = new Device { Id = "d1", Uuid = "d1", Name = "d1", Type = Device.TypeId };
            var host = new Component { Id = "host", Name = "host", Type = "Axes" };
            host.AddComposition(new Composition { Id = "keep-me", Name = "keep-me", Type = "Generic" });
            device.AddComponent(host);

            Assert.DoesNotThrow(() => device.RemoveComposition("does-not-exist"),
                "RemoveComposition on a non-existent ID must be a silent no-op — NormalizeDevice loops over invalid Compositions and would abort onboarding on any throw here.");

            Assert.That(device.GetCompositions().Any(c => c.Id == "keep-me"), Is.True,
                "RemoveComposition on a non-existent ID must not mutate the surviving Compositions.");
        }

        /// <summary>
        /// Pins that <see cref="Device.RemoveComposition"/> is safe against
        /// a Device with no Components and no Compositions at all — both
        /// early-return-on-empty branches inside the override must exit
        /// without throwing. This is a boundary case the FLOOR requires
        /// (§1.0d-trigies-novodecies documented input classes: empty).
        /// </summary>
        [Test]
        public void RemoveComposition_on_empty_device_tree_is_safe()
        {
            var device = new Device { Id = "d1", Uuid = "d1", Name = "d1", Type = Device.TypeId };

            Assert.DoesNotThrow(() => device.RemoveComposition("nothing"),
                "RemoveComposition must exit safely when the Device has neither Components nor Compositions.");
        }

        // --------------------------------------------------------------
        // RemoveDataItem — every code path in the top-level-restoring override.
        // --------------------------------------------------------------

        /// <summary>
        /// Pins that a DataItem attached directly to <see cref="Device.DataItems"/>
        /// is dropped by <see cref="Device.RemoveDataItem"/>. Before the
        /// F-TEST-BUG-2 fix (Device.cs:1049) the override walked ONLY child
        /// Components and silently skipped the Device's own DataItems
        /// collection, so a generic DataItem on the Device itself was
        /// unremovable and <c>NormalizeDevice.Remove</c> was a lie.
        /// </summary>
        [Test]
        public void RemoveDataItem_drops_top_level_DataItem_on_Device()
        {
            var device = new Device { Id = "d1", Uuid = "d1", Name = "d1", Type = Device.TypeId };
            device.AddDataItem(new DataItem { Id = "top-di", Type = "Generic", Category = DataItemCategory.EVENT });

            Assert.That(device.DataItems.Any(d => d.Id == "top-di"), Is.True,
                "precondition — top-level DataItem must be present on Device before removal.");

            device.RemoveDataItem("top-di");

            Assert.That(device.DataItems == null || !device.DataItems.Any(d => d.Id == "top-di"), Is.True,
                "Device.RemoveDataItem must drop a DataItem placed directly on Device.DataItems (F-TEST-BUG-2 fix — the override previously skipped this collection).");
        }

        /// <summary>
        /// Pins deep-Component DataItem removal:
        /// Device → Component → subComponent → subsubComponent → DataItem.
        /// The override iterates <see cref="Device.GetComponents()"/> which is
        /// recursive and returns a flat list of every Component at any
        /// depth; the DataItem must therefore be removed regardless of
        /// how deep the owning Component sits.
        /// </summary>
        [Test]
        public void RemoveDataItem_recurses_into_great_grandchild_Component()
        {
            var device = new Device { Id = "d1", Uuid = "d1", Name = "d1", Type = Device.TypeId };

            var l1 = new Component { Id = "l1", Name = "l1", Type = "Axes" };
            var l2 = new Component { Id = "l2", Name = "l2", Type = "Linear" };
            var l3 = new Component { Id = "l3", Name = "l3", Type = "Motor" };
            l3.AddDataItem(new DataItem { Id = "deep-di", Type = "Generic", Category = DataItemCategory.EVENT });
            l2.AddComponent(l3);
            l1.AddComponent(l2);
            device.AddComponent(l1);

            Assert.That(device.GetDataItems().Any(d => d.Id == "deep-di"), Is.True,
                "precondition — great-grandchild DataItem must be reachable via GetDataItems before removal.");

            device.RemoveDataItem("deep-di");

            Assert.That(device.GetDataItems() == null || !device.GetDataItems().Any(d => d.Id == "deep-di"), Is.True,
                "Device.RemoveDataItem must reach any-depth Component DataItems via the recursive GetComponents() flat list.");
        }

        /// <summary>
        /// Pins idempotency on a non-existent ID: no throw, no mutation
        /// of the surviving DataItems. NormalizeDevice invokes this
        /// per-invalid-DataItem and cannot tolerate an exception path
        /// on missing IDs.
        /// </summary>
        [Test]
        public void RemoveDataItem_nonexistent_id_is_idempotent_no_op()
        {
            var device = new Device { Id = "d1", Uuid = "d1", Name = "d1", Type = Device.TypeId };
            device.AddDataItem(new DataItem { Id = "keep-me", Type = "Generic", Category = DataItemCategory.EVENT });

            Assert.DoesNotThrow(() => device.RemoveDataItem("does-not-exist"),
                "RemoveDataItem on a non-existent ID must be a silent no-op.");

            Assert.That(device.DataItems.Any(d => d.Id == "keep-me"), Is.True,
                "RemoveDataItem on a non-existent ID must not mutate the surviving DataItems.");
        }

        /// <summary>
        /// Pins that <see cref="Device.RemoveDataItem"/> is safe against
        /// a Device with no DataItems and no Components — both
        /// early-return branches inside the override must exit cleanly.
        /// </summary>
        [Test]
        public void RemoveDataItem_on_empty_device_tree_is_safe()
        {
            var device = new Device { Id = "d1", Uuid = "d1", Name = "d1", Type = Device.TypeId };

            Assert.DoesNotThrow(() => device.RemoveDataItem("nothing"),
                "RemoveDataItem must exit safely when the Device has neither DataItems nor Components.");
        }

        // --------------------------------------------------------------
        // Sibling isolation — the recursive RemoveAll(o => o.Id == id)
        // predicate must only match the requested ID and leave every
        // sibling untouched. A regression to a permissive predicate
        // (for example RemoveAll(o => true) inside a wrong overload)
        // would strip every sibling and would pass the single-child
        // tests above; the sibling-isolation shape catches that class.
        // --------------------------------------------------------------

        /// <summary>
        /// Pins that <see cref="Device.RemoveComposition"/> called against
        /// one of two siblings on the top-level <c>Device.Compositions</c>
        /// collection drops only the requested Composition and leaves the
        /// sibling intact.
        /// </summary>
        [Test]
        public void RemoveComposition_top_level_leaves_sibling_Compositions_intact()
        {
            var device = new Device { Id = "d1", Uuid = "d1", Name = "d1", Type = Device.TypeId };
            device.AddComposition(new Composition { Id = "drop-me", Name = "drop-me", Type = "Generic" });
            device.AddComposition(new Composition { Id = "keep-me", Name = "keep-me", Type = "Generic" });

            device.RemoveComposition("drop-me");

            Assert.That(device.Compositions.Any(c => c.Id == "drop-me"), Is.False,
                "RemoveComposition must drop the requested top-level Composition.");
            Assert.That(device.Compositions.Any(c => c.Id == "keep-me"), Is.True,
                "RemoveComposition must not drop siblings of the requested Composition — the RemoveAll predicate is ID-scoped.");
        }

        /// <summary>
        /// Pins the same sibling-isolation invariant on the recursive
        /// nested branch: two Compositions attached to a child Component,
        /// only one requested for removal.
        /// </summary>
        [Test]
        public void RemoveComposition_nested_leaves_sibling_Compositions_intact()
        {
            var device = new Device { Id = "d1", Uuid = "d1", Name = "d1", Type = Device.TypeId };
            var host = new Component { Id = "host", Name = "host", Type = "Axes" };
            host.AddComposition(new Composition { Id = "drop-nested", Name = "drop-nested", Type = "Generic" });
            host.AddComposition(new Composition { Id = "keep-nested", Name = "keep-nested", Type = "Generic" });
            device.AddComponent(host);

            device.RemoveComposition("drop-nested");

            Assert.That(device.GetCompositions().Any(c => c.Id == "drop-nested"), Is.False);
            Assert.That(device.GetCompositions().Any(c => c.Id == "keep-nested"), Is.True,
                "the nested recursion must not drop siblings.");
        }

        /// <summary>
        /// Pins that a Composition with the same ID present at BOTH the
        /// top-level Device.Compositions AND on a nested child Component
        /// is dropped from both locations — RemoveComposition is depth-
        /// unlimited, not first-match. This pins the recursion contract
        /// against a regression that returns on the first match.
        /// </summary>
        [Test]
        public void RemoveComposition_removes_id_from_every_depth_it_appears()
        {
            var device = new Device { Id = "d1", Uuid = "d1", Name = "d1", Type = Device.TypeId };
            device.AddComposition(new Composition { Id = "dup-id", Name = "dup-id-top", Type = "Generic" });
            var host = new Component { Id = "host", Name = "host", Type = "Axes" };
            host.AddComposition(new Composition { Id = "dup-id", Name = "dup-id-nested", Type = "Generic" });
            device.AddComponent(host);

            Assert.That(device.GetCompositions().Count(c => c.Id == "dup-id"), Is.EqualTo(2),
                "precondition — the same ID must be present at both depths.");

            device.RemoveComposition("dup-id");

            var remaining = device.GetCompositions();
            Assert.That(remaining == null || !remaining.Any(c => c.Id == "dup-id"), Is.True,
                "RemoveComposition must remove EVERY occurrence of the ID — top-level AND nested — not just the first match.");
        }

        /// <summary>
        /// Pins that <see cref="Device.RemoveDataItem"/> called against one of
        /// two siblings on the top-level <c>Device.DataItems</c> collection
        /// drops only the requested DataItem and leaves the sibling intact.
        /// </summary>
        [Test]
        public void RemoveDataItem_top_level_leaves_sibling_DataItems_intact()
        {
            var device = new Device { Id = "d1", Uuid = "d1", Name = "d1", Type = Device.TypeId };
            device.AddDataItem(new DataItem { Id = "drop-me", Type = "Generic", Category = DataItemCategory.EVENT });
            device.AddDataItem(new DataItem { Id = "keep-me", Type = "Generic", Category = DataItemCategory.EVENT });

            device.RemoveDataItem("drop-me");

            Assert.That(device.DataItems.Any(d => d.Id == "drop-me"), Is.False,
                "RemoveDataItem must drop the requested top-level DataItem.");
            Assert.That(device.DataItems.Any(d => d.Id == "keep-me"), Is.True,
                "RemoveDataItem must not drop siblings of the requested DataItem.");
        }

        /// <summary>
        /// Pins the same sibling-isolation invariant for the nested branch:
        /// two DataItems attached to a child Component, only one requested
        /// for removal.
        /// </summary>
        [Test]
        public void RemoveDataItem_nested_leaves_sibling_DataItems_intact()
        {
            var device = new Device { Id = "d1", Uuid = "d1", Name = "d1", Type = Device.TypeId };
            var host = new Component { Id = "host", Name = "host", Type = "Axes" };
            host.AddDataItem(new DataItem { Id = "drop-nested", Type = "Generic", Category = DataItemCategory.EVENT });
            host.AddDataItem(new DataItem { Id = "keep-nested", Type = "Generic", Category = DataItemCategory.EVENT });
            device.AddComponent(host);

            device.RemoveDataItem("drop-nested");

            Assert.That(device.GetDataItems().Any(d => d.Id == "drop-nested"), Is.False);
            Assert.That(device.GetDataItems().Any(d => d.Id == "keep-nested"), Is.True,
                "nested-branch removal must not drop siblings.");
        }

        /// <summary>
        /// Pins that a DataItem ID present at BOTH the top-level
        /// Device.DataItems AND on a nested child Component is dropped
        /// from both locations — RemoveDataItem visits every depth,
        /// not just the first match.
        /// </summary>
        [Test]
        public void RemoveDataItem_removes_id_from_every_depth_it_appears()
        {
            var device = new Device { Id = "d1", Uuid = "d1", Name = "d1", Type = Device.TypeId };
            device.AddDataItem(new DataItem { Id = "dup-id", Type = "Generic", Category = DataItemCategory.EVENT });
            var host = new Component { Id = "host", Name = "host", Type = "Axes" };
            host.AddDataItem(new DataItem { Id = "dup-id", Type = "Generic", Category = DataItemCategory.EVENT });
            device.AddComponent(host);

            Assert.That(device.GetDataItems().Count(d => d.Id == "dup-id"), Is.EqualTo(2),
                "precondition — the same ID must be present at both depths.");

            device.RemoveDataItem("dup-id");

            var remaining = device.GetDataItems();
            Assert.That(remaining == null || !remaining.Any(d => d.Id == "dup-id"), Is.True,
                "RemoveDataItem must remove EVERY occurrence of the ID — top-level AND nested.");
        }

        /// <summary>
        /// Pins the intermediate depth-2 Composition-removal branch that
        /// sits BETWEEN the top-level (depth-1) and great-grandchild
        /// (depth-3) coverage above: Device → Component → Composition
        /// directly on the child Component. This is the shape the
        /// original NormalizeDevice-Remove path used before the recursive
        /// fix; a regression to a two-level walk would still pass the
        /// depth-1 top-level test and might pass the depth-3 test via a
        /// different code path — the depth-2 test pins the exact single
        /// level of recursion.
        /// </summary>
        [Test]
        public void RemoveComposition_removes_depth_2_Composition_on_direct_child_Component()
        {
            var device = new Device { Id = "d1", Uuid = "d1", Name = "d1", Type = Device.TypeId };
            var child = new Component { Id = "child", Name = "child", Type = "Axes" };
            child.AddComposition(new Composition { Id = "mid-comp", Name = "mid-comp", Type = "Generic" });
            device.AddComponent(child);

            Assert.That(device.GetCompositions().Any(c => c.Id == "mid-comp"), Is.True,
                "precondition — the depth-2 Composition must be reachable via GetCompositions.");

            device.RemoveComposition("mid-comp");

            var remaining = device.GetCompositions();
            Assert.That(remaining == null || !remaining.Any(c => c.Id == "mid-comp"), Is.True,
                "RemoveComposition must reach depth-2 Compositions attached to a direct child Component.");
        }
    }
}
