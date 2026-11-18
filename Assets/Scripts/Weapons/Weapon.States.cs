using System;

public sealed partial class Weapon
{
    private abstract class WeaponState : StateBase
    {
        protected readonly Weapon W;
        protected WeaponState(Weapon weapon) => W = weapon;
        protected float Time => W.fsm.TimeInState;
    }

    /// <summary>Not in hands: model disabled.</summary>
    private sealed class HiddenState : WeaponState
    {
        public HiddenState(Weapon w) : base(w) { }

        public override void Enter()
        {
            W.ViewModel.SetVisible(false);
            W.AimAmount = 0f;
            W.burstShotsLeft = 0;

            Action callback = W.onHolstered;
            W.onHolstered = null;
            callback?.Invoke();
        }
    }

    private sealed class EquipState : WeaponState
    {
        public EquipState(Weapon w) : base(w) { }

        public override void Enter()
        {
            W.ViewModel.SetVisible(true);
            W.anim.ResetLoopTracking();
            W.anim.PlayAction(WeaponAnimStates.Equip, 0f);
            W.owner.PlaySound(W.Data.equipSound, 1f, 0f);
        }

        public override void Tick(float dt)
        {
            if (Time >= W.Data.equipTime) W.fsm.ChangeState(StateId.Ready);
        }
    }

    /// <summary>Weapon in hands: aim, shoot, start reloads and grenade throws.</summary>
    private sealed class ReadyState : WeaponState
    {
        public ReadyState(Weapon w) : base(w) { }

        public override void Enter()
        {
            W.anim.ResetLoopTracking();
            W.fireAnimationUntil = 0f;
        }

        public override void Tick(float dt)
        {
            if (!W.owner.IsActionBlocked)
            {
                W.wantsAim = W.input.AimHeld;

                if (W.input.ReloadPressed && W.CanReload)
                {
                    W.fsm.ChangeState(StateId.Reloading);
                    return;
                }
                if (W.input.GrenadePressed && W.owner.CanThrowGrenade)
                {
                    W.fsm.ChangeState(StateId.ThrowingGrenade);
                    return;
                }
                W.HandleTrigger();
            }

            if (W.fsm.IsIn(StateId.Ready)) W.UpdateLocomotionAnimation();
        }

        public override void Exit()
        {
            W.burstShotsLeft = 0;
        }
    }

    private sealed class ReloadState : WeaponState
    {
        private float duration;

        public ReloadState(Weapon w) : base(w) { }

        public override void Enter()
        {
            bool empty = W.Magazine == 0;
            duration = empty ? W.Data.reloadEmptyTime : W.Data.reloadTime;
            W.anim.PlayAction(W.Data.actionCrossFade, empty ? ReloadEmptyStates : ReloadStates);
            W.owner.PlaySound(W.Data.reloadSound, 1f, 0f);
        }

        public override void Tick(float dt)
        {
            if (Time >= duration)
            {
                W.FinishReload();
                W.fsm.ChangeState(StateId.Ready);
                return;
            }

            // Cancel the reload by shooting (keeps the remaining rounds, adds none).
            if (W.Data.fireCancelsReload && W.input.FirePressed && W.Magazine > 0 && !W.owner.IsActionBlocked)
                W.fsm.ChangeState(StateId.Ready);
        }
    }

    private sealed class HolsterState : WeaponState
    {
        public HolsterState(Weapon w) : base(w) { }

        public override void Enter()
        {
            W.anim.PlayAction(WeaponAnimStates.Holster, W.Data.actionCrossFade);
        }

        public override void Tick(float dt)
        {
            if (Time >= W.Data.holsterTime) W.fsm.ChangeState(StateId.Hidden);
        }
    }

    /// <summary>Plays GrenadeThrow on this weapon's arms and releases the grenade at the right moment.</summary>
    private sealed class GrenadeState : WeaponState
    {
        private bool released;

        public GrenadeState(Weapon w) : base(w) { }

        public override void Enter()
        {
            released = false;
            W.anim.PlayAction(WeaponAnimStates.GrenadeThrow, W.Data.actionCrossFade);
            W.ViewModel.GrenadeReleaseRequested += Release;
        }

        public override void Tick(float dt)
        {
            if (!released && Time >= W.Data.grenadeReleaseTime) Release();
            if (Time >= W.Data.grenadeThrowTime) W.fsm.ChangeState(StateId.Ready);
        }

        public override void Exit()
        {
            W.ViewModel.GrenadeReleaseRequested -= Release;
        }

        private void Release()
        {
            if (released) return;
            released = true;
            W.owner.ReleaseGrenade(W.ViewModel.GrenadeSpawnPoint);
        }
    }
}
