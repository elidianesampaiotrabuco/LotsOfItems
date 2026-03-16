using System.Collections.Generic;
using UnityEngine;

namespace LotsOfItems.CustomItems.Teleporters;

public class ITM_GatewayTeleporter : ITM_GenericTeleporter
{
    protected override void Teleport()
    {
        if (pm.ec.Elevators.Count == 0)
        {
            base.Teleport();
            return;
        }

        exclusiveElevators.Clear();
        for (int i = 0; i < pm.ec.Elevators.Count; i++)
        {
            var el = pm.ec.Elevators[i];
            if (lastPickedElevator != el && el.IsOpen && el.Powered)
                exclusiveElevators.Add(el);
        }

        if (exclusiveElevators.Count == 0)
            exclusiveElevators.AddRange(pm.ec.Elevators); // Just ignore all conditions

        lastPickedElevator = exclusiveElevators[Random.Range(0, exclusiveElevators.Count)];
        pm.Teleport(pm.ec.CellFromPosition(lastPickedElevator.Door.position - lastPickedElevator.Door.direction.ToIntVector2()).FloorWorldPosition);
        Singleton<CoreGameManager>.Instance.audMan.PlaySingle(audTeleport);
    }

    Elevator lastPickedElevator;
    readonly List<Elevator> exclusiveElevators = [];
}