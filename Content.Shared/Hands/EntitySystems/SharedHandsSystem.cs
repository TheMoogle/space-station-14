using Content.Shared.ActionBlocker;
using Content.Shared.Administration.Logs;
using Content.Shared.Hands.Components;
using Content.Shared.Interaction;
using Robust.Shared.Containers;
using Robust.Shared.Input.Binding;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Content.Shared.Hands.EntitySystems;

public abstract partial class SharedHandsSystem : EntitySystem
{
    [Dependency] private readonly SharedAdminLogSystem _adminLogSystem = default!;
    [Dependency] private readonly SharedInteractionSystem _interactionSystem = default!;
    [Dependency] private readonly ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private readonly SharedContainerSystem _containerSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        InitializeInteractions();
    }

    public override void Shutdown()
    {
        base.Shutdown();
        CommandBinds.Unregister<SharedHandsSystem>();
    }

    public void AddHand(EntityUid uid, string handName, HandLocation handLocation, SharedHandsComponent? handsComp = null)
    {
        if (!Resolve(uid, ref handsComp, false))
            return;

        if (handsComp.Hands.ContainsKey(handName))
            return;

        var container = _containerSystem.EnsureContainer<ContainerSlot>(uid, handName);
        container.OccludesLight = false;

        var newHand = new Hand(handName, handLocation, container);
        handsComp.Hands.Add(handName, newHand);
        handsComp.SortedHands.Add(handName);

        if (handsComp.ActiveHand == null)
            SetActiveHand(uid, newHand, handsComp);

        RaiseLocalEvent(uid, new HandCountChangedEvent(uid), false);
        Dirty(handsComp);
    }

    public void RemoveHand(EntityUid uid, string handName, SharedHandsComponent? handsComp = null)
    {
        if (!Resolve(uid, ref handsComp, false))
            return;

        if (!handsComp.Hands.Remove(handName, out var hand))
            return;

        TryDrop(uid, hand, null, false, true, handsComp);
        hand.Container?.Shutdown();
        handsComp.SortedHands.Remove(hand.Name);

        if (handsComp.ActiveHand == hand)
            TrySetActiveHand(uid, handsComp.SortedHands.FirstOrDefault(), handsComp);

        RaiseLocalEvent(uid, new HandCountChangedEvent(uid), false);
        Dirty(handsComp);
    }

    private void HandleSetHand(RequestSetHandEvent msg, EntitySessionEventArgs eventArgs)
    {
        if (eventArgs.SenderSession.AttachedEntity == null)
            return;

        TrySetActiveHand(eventArgs.SenderSession.AttachedEntity.Value, msg.HandName);
    }

    /// <summary>
    ///     Get any empty hand. Prioritizes the currently active hand.
    /// </summary>
    public bool TryGetEmptyHand(EntityUid uid, [NotNullWhen(true)] out Hand? emptyHand, SharedHandsComponent? handComp = null)
    {
        emptyHand = null;
        if (!Resolve(uid, ref handComp, false))
            return false;

        foreach (var hand in EnumerateHands(uid, handComp))
        {
            if (hand.IsEmpty)
            {
                emptyHand = hand;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Enumerate over hands, starting with the currently active hand.
    /// </summary>
    public IEnumerable<Hand> EnumerateHands(EntityUid uid, SharedHandsComponent? handsComp = null)
    {
        if (!Resolve(uid, ref handsComp, false))
            yield break;

        if (handsComp.ActiveHand != null)
            yield return handsComp.ActiveHand;

        foreach (var name in handsComp.SortedHands)
        {
            if (name != handsComp.ActiveHand?.Name)
                yield return handsComp.Hands[name];
        }
    }

    /// <summary>
    ///     Enumerate over hands, with the active hand being first.
    /// </summary>
    public IEnumerable<EntityUid> EnumerateHeld(EntityUid uid, SharedHandsComponent? handsComp = null)
    {
        if (!Resolve(uid, ref handsComp, false))
            yield break;

        if (handsComp.ActiveHandEntity != null)
            yield return handsComp.ActiveHandEntity.Value;

        foreach (var name in handsComp.SortedHands)
        {
            if (name == handsComp.ActiveHand?.Name)
                continue;

            if (handsComp.Hands[name].HeldEntity is EntityUid held)
                yield return held;
        }
    }

    /// <summary>
    ///     Set the currently active hand and raise hand (de)selection events directed at the held entities.
    /// </summary>
    /// <returns>True if the active hand was set to a NEW value. Setting it to the same value returns false and does
    /// not trigger interactions.</returns>
    public virtual bool TrySetActiveHand(EntityUid uid, string? name, SharedHandsComponent? handComp = null)
    {
        if (!Resolve(uid, ref handComp))
            return false;

        if (name == handComp.ActiveHand?.Name)
            return false;

        Hand? hand = null;
        if (name != null && !handComp.Hands.TryGetValue(name, out hand))
            return false;

        return SetActiveHand(uid, hand, handComp);
    }

    /// <summary>
    ///     Set the currently active hand and raise hand (de)selection events directed at the held entities.
    /// </summary>
    /// <returns>True if the active hand was set to a NEW value. Setting it to the same value returns false and does
    /// not trigger interactions.</returns>
    public virtual bool SetActiveHand(EntityUid uid, Hand? hand, SharedHandsComponent? handComp = null)
    {
        if (!Resolve(uid, ref handComp))
            return false;

        if (hand == handComp.ActiveHand)
            return false;

        if (handComp.ActiveHand?.HeldEntity is EntityUid held)
            RaiseLocalEvent(held, new HandDeselectedEvent(uid), false);

        if (hand == null)
        {
            handComp.ActiveHand = null;
            return true;
        }

        handComp.ActiveHand = hand;

        if (hand.HeldEntity != null)
            RaiseLocalEvent(hand.HeldEntity.Value, new HandSelectedEvent(uid), false);

        Dirty(handComp);
        return true;
    }

    public bool IsHolding(EntityUid uid, EntityUid? entity, [NotNullWhen(true)] out Hand? inHand, SharedHandsComponent? handsComp = null)
    {
        inHand = null;
        if (!Resolve(uid, ref handsComp, false))
            return false;

        foreach (var hand in handsComp.Hands.Values)
        {
            if (hand.HeldEntity == entity)
            {
                inHand = hand;
                return true;
            }
        }

        return false;
    }
}
