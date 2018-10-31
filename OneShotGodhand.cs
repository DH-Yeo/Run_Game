//
// 개발한 코드 중 Godhand라는 Skill 관련한 부분만 짜집기 한 파일입니다.
//

using UnityEngine;
using System.Collections;

public class OneShotGodhand : Skill
{
    public override void UseSkill(int targetViewID, int attackerViewID)
    {
        base.UseSkill(targetViewID, attackerViewID);

        if (PhotonView.Find(attackerViewID).isMine)
        {
            RunCoreSpawn.myPlayer.GetComponent<CharacterSkillListener>().UseGodhand(this);
            RunCoreSpawn.cameraCutScene.RunCutScene(5f, target);
        }

        if (PhotonView.Find(targetViewID).isMine)
        {
            RunCoreSpawn.myPlayer.GetComponent<CharacterSkillListener>().OnAlertGodhand(this);
            //RunCoreSpawn.myPlayer.GetComponent<CharacterElementListener>().OnHitSkill(this, attackerViewID);
        }
    }

    public override void UseSkill(GameObject targetInst, int attackerViewID)
    {
        base.UseSkill(targetInst, attackerViewID);

        // Show camera cut scene
        if (PhotonView.Find(attackerViewID).isMine)
        {
            RunCoreSpawn.myPlayer.GetComponent<CharacterSkillListener>().UseGodhand(this);
            RunCoreSpawn.cameraCutScene.RunCutScene(5f, target);
        }

        targetInst.GetComponent<CharacterSkillListener>().OnAlertGodhand(this);
        //targetInst.GetComponent<CharacterElementListener>().OnHitSkill(this, attackerViewID);
    }
}


////////////////////////////////////////////////////////////////////////////////////////////////////////////
//
// CharacterSkillListener.cs 중 Godhand 관련 변수 및 함수 (Godhand 스킬 사용 이후)
//
////////////////////////////////////////////////////////////////////////////////////////////////////////////

public class CharacterSkillListener : MonoBehaviour
{
    // 현재 플레이어의 Camera
    private GameObject myCamera;
    private GameObject uiCamera;

    // 플레이어의 core components
    private CharacterEffect charEffect;                                // 캐릭터에게 나타나는 이펙트 관리
    private CharacterMotor charMotor;                                  // 캐릭터의 이동 관리
    private CharacterModel charModel;                                  // 캐릭터 모델 관리
    private CharacterEventManager charEventMng;                        // 캐릭터에 발생한 이벤트 관리
    private CharacterInvincible charInvincible;                        // 캐릭터 무적상태 관리
    private CharacterMapObjectListener charMapObjListener;
    private CharacterFollowingMonsterManager charFollowingMonsterMng;
    private SkillSound skillSound;

    // 캐릭터 모델 관련 components
    private CharacterOpacity charOpacity;                              // 캐릭터의 무적상태 관련 효과 (shader) 관리
    private CharacterAnimation charAnim;                               // 캐릭터의 애니메이션 관리

    // 카메라 관련 components
    private CameraEffect camEffect;                                    // 카메라 이펙트 관리
    private CameraAnimation camAnim;                                   // 카메라 애니메이션(진동 등) 관리
    private CameraFieldOfViewManager camFovManager;

    //
    // 중간 생략
    //

    // GodHand 스킬을 사용한 시전자에게 작용하는 함수
    public void UseGodhand(Skill skill)
    {
        float durationTime = (float)skill.durationTime[0];
        skill.PlayUseEffect();

        skillSound.UseGodHand();
        charEventMng.UseGodhand(durationTime);
        charAnim.UseSkill(charAnim.useGodhand, CharacterStateEnum.UseGodhand);
        charAnim.ReserveAnimation(charAnim.run, CharacterStateEnum.Run, durationTime);

        charFollowingMonsterMng.UseSkill(skill.characterID, durationTime, durationTime);
    }

    // GodHand 스킬을 맞기 전(타겟팅 되었을 때) 나타나는 효과
    public void OnTargetByGodHand(Skill skill)
    {
        float durationTime = CalculateDebuffDuration((float)skill.durationTime[0]);
        skill.PlaySkillEffect();

        charAnim.TargetBySkill(charAnim.targetByGodhand, CharacterStateEnum.TargetByGodhand, (float)skill.durationTime[0]);
        charEventMng.TargettedByGodhand();
    }

    // GodHand 스킬을 맞기 직전 나타나는 효과
    public void OnAlertGodhand(Skill skill)
    {
        charEffect.DeleteAllEffect();

        float durationTime = CalculateDebuffDuration((float)skill.durationTime[0]);
        skill.PlayAlertEffect();

        skillSound.OnAlertGodhand();
        charMotor.OnHitStunSkill(durationTime);
        charAnim.AlertSkill(charAnim.alertGodhand, CharacterStateEnum.AlertGodhand);
        charAnim.ReserveAnimation(charAnim.run, CharacterStateEnum.Run, durationTime);
        charEventMng.HitSkill(durationTime);
        charEventMng.OnHitGodhand(durationTime);
        charMapObjListener.OnHitStunSkill();
        charInvincible.OnHitStunSkill(durationTime);
        camAnim.GlobalShakeCamera(2.5f, 1.1f);
        camAnim.PlayAnimation(skill.cameraWork);
    }

    // GodHand 스킬을 맞았을 때 효과
    public void OnHitGodhandSkill(Skill skill)
    {
        float durationTime = CalculateDebuffDuration((float)skill.durationTime[0]);
        skill.PlayOnHitEffect();

        skillSound.OnHitGodHand();

        charOpacity.OnHitStunSkill();
        charAnim.OnHitSkill(charAnim.hitGodhand, CharacterStateEnum.HitGodhand, durationTime);
        camAnim.GlobalShakeCamera(1.5f, 0.5f);
        camEffect.OnHitGodhand();
    }

    //
    // 이하 생략
    //
}

////////////////////////////////////////////////////////////////////////////////////////////////////////////