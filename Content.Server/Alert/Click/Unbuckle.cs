﻿using Content.Server.Buckle.Components;
using Content.Shared.Alert;
using JetBrains.Annotations;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Server.Alert.Click
{
    /// <summary>
    /// Unbuckles if player is currently buckled.
    /// </summary>
	[UsedImplicitly]
    [DataDefinition]
    public class Unbuckle : IAlertClick
    {
        public void AlertClicked(ClickAlertEventArgs args)
        {
            if (IoCManager.Resolve<IEntityManager>().TryGetComponent(args.Player, out BuckleComponent? buckle))
            {
                buckle.TryUnbuckle(args.Player);
            }
        }
    }
}
