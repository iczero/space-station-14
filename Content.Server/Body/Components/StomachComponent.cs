﻿using System.Collections.Generic;
using Content.Server.Body.Systems;
using Content.Shared.Body.Components;
using Content.Shared.FixedPoint;
using Robust.Shared.Analyzers;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.ViewVariables;

namespace Content.Server.Body.Components
{
    [RegisterComponent, Friend(typeof(StomachSystem))]
    public class StomachComponent : Component
    {
        public override string Name => "Stomach";

        public float AccumulatedFrameTime;

        /// <summary>
        ///     How fast should this component update, in seconds?
        /// </summary>
        [DataField("updateInterval")]
        public float UpdateInterval = 1.0f;

        /// <summary>
        ///     What solution should this stomach push reagents into, on the body?
        /// </summary>
        [DataField("bodySolutionName")]
        public string BodySolutionName = SharedBloodstreamComponent.DefaultSolutionName;

        /// <summary>
        ///     Initial internal solution storage volume
        /// </summary>
        [DataField("maxVolume")]
        public FixedPoint2 InitialMaxVolume { get; private set; } = FixedPoint2.New(100);

        /// <summary>
        ///     Time in seconds between reagents being ingested and them being
        ///     transferred to <see cref="SharedBloodstreamComponent"/>
        /// </summary>
        [DataField("digestionDelay")]
        public float DigestionDelay = 20;

        /// <summary>
        ///     Used to track how long each reagent has been in the stomach
        /// </summary>
        [ViewVariables]
        public readonly List<ReagentDelta> ReagentDeltas = new();

        /// <summary>
        ///     Used to track quantity changes when ingesting & digesting reagents
        /// </summary>
        public class ReagentDelta
        {
            public readonly string ReagentId;
            public readonly FixedPoint2 Quantity;
            public float Lifetime { get; private set; }

            public ReagentDelta(string reagentId, FixedPoint2 quantity)
            {
                ReagentId = reagentId;
                Quantity = quantity;
                Lifetime = 0.0f;
            }

            public void Increment(float delta) => Lifetime += delta;
        }
    }
}
