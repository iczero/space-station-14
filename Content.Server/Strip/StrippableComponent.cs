using System.Collections.Generic;
using System.Threading;
using Content.Server.Cuffs.Components;
using Content.Server.DoAfter;
using Content.Server.Hands.Components;
using Content.Server.Inventory.Components;
using Content.Server.Items;
using Content.Server.UserInterface;
using Content.Shared.ActionBlocker;
using Content.Shared.DragDrop;
using Content.Shared.Hands.Components;
using Content.Shared.Popups;
using Content.Shared.Strip.Components;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.ViewVariables;
using static Content.Shared.Inventory.EquipmentSlotDefines;

namespace Content.Server.Strip
{
    [RegisterComponent]
    [ComponentReference(typeof(SharedStrippableComponent))]
    public sealed class StrippableComponent : SharedStrippableComponent
    {
        public const float StripDelay = 2f;

        // TODO: This component needs localization.

        [ViewVariables]
        private BoundUserInterface? UserInterface => Owner.GetUIOrNull(StrippingUiKey.Key);

        protected override void Initialize()
        {
            base.Initialize();

            if (UserInterface != null)
            {
                UserInterface.OnReceiveMessage += HandleUserInterfaceMessage;
            }

            Owner.EnsureComponentWarn<InventoryComponent>();
            Owner.EnsureComponentWarn<HandsComponent>();
            Owner.EnsureComponentWarn<CuffableComponent>();

            if (Owner.TryGetComponent(out CuffableComponent? cuffed))
            {
                cuffed.OnCuffedStateChanged += UpdateSubscribed;
            }

            if (Owner.TryGetComponent(out InventoryComponent? inventory))
            {
                inventory.OnItemChanged += UpdateSubscribed;
            }

            if (Owner.TryGetComponent(out HandsComponent? hands))
            {
                hands.OnItemChanged += UpdateSubscribed;
            }

            // Initial update.
            UpdateSubscribed();
        }

        private void UpdateSubscribed()
        {
            if (UserInterface == null)
            {
                return;
            }

            var inventory = GetInventorySlots();
            var hands = GetHandSlots();
            var cuffs = GetHandcuffs();

            UserInterface.SetState(new StrippingBoundUserInterfaceState(inventory, hands, cuffs));
        }

        public override bool Drop(DragDropEvent args)
        {
            if (!args.User.TryGetComponent(out ActorComponent? actor)) return false;

            OpenUserInterface(actor.PlayerSession);
            return true;
        }

        private Dictionary<EntityUid, string> GetHandcuffs()
        {
            var dictionary = new Dictionary<EntityUid, string>();

            if (!Owner.TryGetComponent(out CuffableComponent? cuffed))
            {
                return dictionary;
            }

            foreach (IEntity entity in cuffed.StoredEntities)
            {
                dictionary.Add(entity.Uid, entity.Name);
            }

            return dictionary;
        }

        private Dictionary<Slots, string> GetInventorySlots()
        {
            var dictionary = new Dictionary<Slots, string>();

            if (!Owner.TryGetComponent(out InventoryComponent? inventory))
            {
                return dictionary;
            }

            foreach (var slot in inventory.Slots)
            {
                dictionary[slot] = inventory.GetSlotItem(slot)?.Owner.Name ?? "None";
            }

            return dictionary;
        }

        private Dictionary<string, string> GetHandSlots()
        {
            var dictionary = new Dictionary<string, string>();

            if (!Owner.TryGetComponent(out HandsComponent? hands))
            {
                return dictionary;
            }

            foreach (var hand in hands.HandNames)
            {
                var owner = hands.GetItem(hand)?.Owner;

                if (owner?.HasComponent<HandVirtualItemComponent>() ?? true)
                {
                    dictionary[hand] = "None";
                    continue;
                }

                dictionary[hand] = owner.Name;
            }

            return dictionary;
        }

        public void OpenUserInterface(IPlayerSession session)
        {
            UserInterface?.Open(session);
        }

        /// <summary>
        ///     Places item in user's active hand to an inventory slot.
        /// </summary>
        private async void PlaceActiveHandItemInInventory(IEntity user, Slots slot)
        {
            var inventory = Owner.GetComponent<InventoryComponent>();
            var userHands = user.GetComponent<HandsComponent>();
            var item = userHands.GetActiveHand;

            bool Check()
            {
                if (!EntitySystem.Get<ActionBlockerSystem>().CanInteract(user.Uid))
                    return false;

                if (item == null)
                {
                    user.PopupMessageCursor(Loc.GetString("strippable-component-not-holding-anything"));
                    return false;
                }

                if (!userHands.CanDrop(userHands.ActiveHand!))
                {
                    user.PopupMessageCursor(Loc.GetString("strippable-component-cannot-drop"));
                    return false;
                }

                if (!inventory.HasSlot(slot))
                    return false;

                if (inventory.TryGetSlotItem(slot, out ItemComponent? _))
                {
                    user.PopupMessageCursor(Loc.GetString("strippable-component-item-slot-occupied",("owner", Owner)));
                    return false;
                }

                if (!inventory.CanEquip(slot, item, false))
                {
                    user.PopupMessageCursor(Loc.GetString("strippable-component-cannot-equip-message",("owner", Owner)));
                    return false;
                }

                return true;
            }

            var doAfterSystem = EntitySystem.Get<DoAfterSystem>();

            var doAfterArgs = new DoAfterEventArgs(user, StripDelay, CancellationToken.None, Owner)
            {
                ExtraCheck = Check,
                BreakOnStun = true,
                BreakOnDamage = true,
                BreakOnTargetMove = true,
                BreakOnUserMove = true,
                NeedHand = true,
            };

            var result = await doAfterSystem.WaitDoAfter(doAfterArgs);
            if (result != DoAfterStatus.Finished) return;

            userHands.Drop(item!.Owner, false);
            inventory.Equip(slot, item!.Owner, false);

            UpdateSubscribed();
        }

        /// <summary>
        ///     Places item in user's active hand in one of the entity's hands.
        /// </summary>
        private async void PlaceActiveHandItemInHands(IEntity user, string hand)
        {
            var hands = Owner.GetComponent<HandsComponent>();
            var userHands = user.GetComponent<HandsComponent>();
            var item = userHands.GetActiveHand;

            bool Check()
            {
                if (!EntitySystem.Get<ActionBlockerSystem>().CanInteract(user.Uid))
                    return false;

                if (item == null)
                {
                    user.PopupMessageCursor(Loc.GetString("strippable-component-not-holding-anything"));
                    return false;
                }

                if (!userHands.CanDrop(userHands.ActiveHand!))
                {
                    user.PopupMessageCursor(Loc.GetString("strippable-component-cannot-drop"));
                    return false;
                }

                if (!hands.HasHand(hand))
                {
                    return false;
                }

                if (hands.TryGetItem(hand, out var _))
                {
                    user.PopupMessageCursor(Loc.GetString("strippable-component-item-slot-occupied-message", ("owner", Owner)));
                    return false;
                }

                if (!hands.CanPickupEntity(hand, item.Owner, checkActionBlocker: false))
                {
                    user.PopupMessageCursor(Loc.GetString("strippable-component-cannot-put-message",("owner", Owner)));
                    return false;
                }

                return true;
            }

            var doAfterSystem = EntitySystem.Get<DoAfterSystem>();

            var doAfterArgs = new DoAfterEventArgs(user, StripDelay, CancellationToken.None, Owner)
            {
                ExtraCheck = Check,
                BreakOnStun = true,
                BreakOnDamage = true,
                BreakOnTargetMove = true,
                BreakOnUserMove = true,
                NeedHand = true,
            };

            var result = await doAfterSystem.WaitDoAfter(doAfterArgs);
            if (result != DoAfterStatus.Finished) return;

            userHands.Drop(hand);
            hands.TryPickupEntity(hand, item!.Owner, checkActionBlocker: false);
            UpdateSubscribed();
        }

        /// <summary>
        ///     Takes an item from the inventory and places it in the user's active hand.
        /// </summary>
        private async void TakeItemFromInventory(IEntity user, Slots slot)
        {
            var inventory = Owner.GetComponent<InventoryComponent>();
            var userHands = user.GetComponent<HandsComponent>();

            bool Check()
            {
                if (!EntitySystem.Get<ActionBlockerSystem>().CanInteract(user.Uid))
                    return false;

                if (!inventory.HasSlot(slot))
                    return false;

                if (!inventory.TryGetSlotItem(slot, out ItemComponent? itemToTake))
                {
                    user.PopupMessageCursor(Loc.GetString("strippable-component-item-slot-free-message",("owner", Owner)));
                    return false;
                }

                if (!inventory.CanUnequip(slot, false))
                {
                    user.PopupMessageCursor(Loc.GetString("strippable-component-cannot-unequip-message",("owner", Owner)));
                    return false;
                }

                return true;
            }

            var doAfterSystem = EntitySystem.Get<DoAfterSystem>();

            var doAfterArgs = new DoAfterEventArgs(user, StripDelay, CancellationToken.None, Owner)
            {
                ExtraCheck = Check,
                BreakOnStun = true,
                BreakOnDamage = true,
                BreakOnTargetMove = true,
                BreakOnUserMove = true,
            };

            var result = await doAfterSystem.WaitDoAfter(doAfterArgs);
            if (result != DoAfterStatus.Finished) return;

            var item = inventory.GetSlotItem(slot);
            inventory.Unequip(slot, false);

            if (item != null)
            {
                userHands.PutInHandOrDrop(item);
            }

            UpdateSubscribed();
        }

        /// <summary>
        ///     Takes an item from a hand and places it in the user's active hand.
        /// </summary>
        private async void TakeItemFromHands(IEntity user, string hand)
        {
            var hands = Owner.GetComponent<HandsComponent>();
            var userHands = user.GetComponent<HandsComponent>();

            bool Check()
            {
                if (!EntitySystem.Get<ActionBlockerSystem>().CanInteract(user.Uid))
                    return false;

                if (!hands.HasHand(hand))
                    return false;

                if (!hands.TryGetItem(hand, out var heldItem))
                {
                    user.PopupMessageCursor(Loc.GetString("strippable-component-item-slot-free-message",("owner", Owner)));
                    return false;
                }

                if (heldItem.Owner.HasComponent<HandVirtualItemComponent>())
                    return false;

                if (!hands.CanDrop(hand, false))
                {
                    user.PopupMessageCursor(Loc.GetString("strippable-component-cannot-drop-message",("owner", Owner)));
                    return false;
                }

                return true;
            }

            var doAfterSystem = EntitySystem.Get<DoAfterSystem>();

            var doAfterArgs = new DoAfterEventArgs(user, StripDelay, CancellationToken.None, Owner)
            {
                ExtraCheck = Check,
                BreakOnStun = true,
                BreakOnDamage = true,
                BreakOnTargetMove = true,
                BreakOnUserMove = true,
            };

            var result = await doAfterSystem.WaitDoAfter(doAfterArgs);
            if (result != DoAfterStatus.Finished) return;

            var item = hands.GetItem(hand);
            hands.Drop(hand, false);
            userHands.PutInHandOrDrop(item!);
            UpdateSubscribed();
        }

        private void HandleUserInterfaceMessage(ServerBoundUserInterfaceMessage obj)
        {
            var user = obj.Session.AttachedEntity;
            if (user == null || !(user.TryGetComponent(out HandsComponent? userHands))) return;

            var placingItem = userHands.GetActiveHand != null;

            switch (obj.Message)
            {
                case StrippingInventoryButtonPressed inventoryMessage:

                    if (Owner.TryGetComponent<InventoryComponent>(out var inventory))
                    {
                        if (inventory.TryGetSlotItem(inventoryMessage.Slot, out ItemComponent? _))
                            placingItem = false;

                        if (placingItem)
                            PlaceActiveHandItemInInventory(user, inventoryMessage.Slot);
                        else
                            TakeItemFromInventory(user, inventoryMessage.Slot);
                    }
                    break;

                case StrippingHandButtonPressed handMessage:

                    if (Owner.TryGetComponent<HandsComponent>(out var hands))
                    {
                        if (hands.TryGetItem(handMessage.Hand, out _))
                            placingItem = false;

                        if (placingItem)
                            PlaceActiveHandItemInHands(user, handMessage.Hand);
                        else
                            TakeItemFromHands(user, handMessage.Hand);
                    }
                    break;

                case StrippingHandcuffButtonPressed handcuffMessage:

                    if (Owner.TryGetComponent<CuffableComponent>(out var cuffed))
                    {
                        foreach (var entity in cuffed.StoredEntities)
                        {
                            if (entity.Uid == handcuffMessage.Handcuff)
                            {
                                cuffed.TryUncuff(user, entity);
                                return;
                            }
                        }
                    }
                    break;
            }
        }
    }
}
