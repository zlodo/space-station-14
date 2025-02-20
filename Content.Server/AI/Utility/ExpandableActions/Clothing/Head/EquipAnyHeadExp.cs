using System;
using System.Collections.Generic;
using Content.Server.AI.Utility.Actions;
using Content.Server.AI.Utility.Actions.Clothing.Head;
using Content.Server.AI.Utility.Considerations;
using Content.Server.AI.Utility.Considerations.Clothing;
using Content.Server.AI.WorldState;
using Content.Server.AI.WorldState.States;
using Content.Server.AI.WorldState.States.Inventory;
using Content.Server.Clothing.Components;
using Content.Shared.Inventory;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;

namespace Content.Server.AI.Utility.ExpandableActions.Clothing.Head
{
    /// <summary>
    /// Equip any head item currently in our inventory
    /// </summary>
    public sealed class EquipAnyHeadExp : ExpandableUtilityAction
    {
        public override float Bonus => UtilityAction.NormalBonus;

        protected override IEnumerable<Func<float>> GetCommonConsiderations(Blackboard context)
        {
            var considerationsManager = IoCManager.Resolve<ConsiderationsManager>();

            return new[] {
                considerationsManager.Get<ClothingInSlotCon>().Slot(EquipmentSlotDefines.Slots.HEAD, context)
                .InverseBoolCurve(context),
            };
        }

        public override IEnumerable<UtilityAction> GetActions(Blackboard context)
        {
            var owner = context.GetState<SelfState>().GetValue();

            foreach (var entity in context.GetState<EnumerableInventoryState>().GetValue())
            {
                if (IoCManager.Resolve<IEntityManager>().TryGetComponent(entity, out ClothingComponent? clothing) &&
                    (clothing.SlotFlags & EquipmentSlotDefines.SlotFlags.HEAD) != 0)
                {
                    yield return new EquipHead {Owner = owner, Target = entity, Bonus = Bonus};
                }
            }
        }
    }
}
