using System.Linq;
using Content.Shared.Physics;
using Content.Shared.Tag;
using JetBrains.Annotations;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Broadphase;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared.Construction.Conditions
{
    [UsedImplicitly]
    [DataDefinition]
    public class WallmountCondition : IConstructionCondition
    {
        public bool Condition(IEntity user, EntityCoordinates location, Direction direction)
        {
            var entManager = IoCManager.Resolve<IEntityManager>();

            // get blueprint and user position
            var userWorldPosition = user.Transform.WorldPosition;
            var objWorldPosition = location.ToMap(entManager).Position;

            // find direction from user to blueprint
            var userToObject = (objWorldPosition - userWorldPosition);

            // dot product will be positive if user direction and blueprint are co-directed
            var dotProd = Vector2.Dot(direction.ToVec(), userToObject);
            if (dotProd > 0)
                return false;

            // now we need to check that user actually tries to build wallmount on a wall
            var physics = EntitySystem.Get<SharedPhysicsSystem>();
            var rUserToObj = new CollisionRay(userWorldPosition, userToObject.Normalized, (int) CollisionGroup.Impassable);
            var length = userToObject.Length;
            var userToObjRaycastResults = physics.IntersectRayWithPredicate(user.Transform.MapID, rUserToObj, maxLength: length,
                predicate: (e) => !e.HasTag("Wall"));
            if (!userToObjRaycastResults.Any())
                return false;

            // get this wall entity
            var targetWall = userToObjRaycastResults.First().HitEntity;

            // check that we didn't try to build wallmount that facing another adjacent wall
            var rAdjWall = new CollisionRay(objWorldPosition, direction.ToVec(), (int) CollisionGroup.Impassable);
            var adjWallRaycastResults = physics.IntersectRayWithPredicate(user.Transform.MapID, rAdjWall, maxLength: 0.5f,
               predicate: (e) => e == targetWall || !e.HasTag("Wall"));
            return !adjWallRaycastResults.Any();
        }

        public ConstructionGuideEntry? GenerateGuideEntry()
        {
            return new ConstructionGuideEntry()
            {
                Localization = "construction-step-condition-wallmount",
            };
        }
    }
}
