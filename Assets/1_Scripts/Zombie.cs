﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class Zombie : MonoBehaviour
{
    public enum State
    {
        Idle = 0,
        Walk = 1,
        Trace = 2,
        BattleMode = 3,
        Attack = 4,
        Dead = 5
    }

    // public 접근자는 기획자가 조정
    public bool isDead = false;
    public float HP;                                        // 좀비의 체력 
    public float ap;                                        // 좀비의 공격력
    public float walkSpeed;                                 // 대기상태의 걷기 속도
    public float runSpeed;                                  // 추적상태의 달리기 속도
    public float attackRange;                               // 좀비 공격의 범위 
    public float normalSearchDistance;                      // 좀비가 주인공을 탐색할 거리
    public float awareSearchDistance;                       // 좀비 피격상태에서의 탐색할 거리
    public float normalSearchAngle;                         // 좀비의 시야각 (플레이어를 감지할 시야각) 
    public float awareSearchAngle;                          // 좀비의 시야각 (플레이어를 감지할 시야각)
    private float currentSearchDistance;
    private float currentSearchAngle;

    public float walkDelayMin = 2.0f;                       // 좀비의 걷기 최소시간
    public float walkDelayMax = 3.5f;                       // 좀비의 걷기 최대시간
    public float idleDelayMin = 1.5f;                       // 좀비 Idle 최소시간
    public float idleDelayMax = 4.0f;                       // 좀비 Idle 최대시간 

    private float awareTime;                                // 공격받았을때 거리와 관계없이 무조건 주인공을 인식할 시간
    private float idleWalkDelay;                            // 좀비가 주변을 걷는 시간(랜덤범위)
    private float idleDelay;                                // 좀비가 제자리에 머무는 시간(랜덤범위)
    private float timeFlag;                                 // 비교 기준이 될 시간

    public float thinkIntervalTime;                         // 좀비의 생각 주기 (생각을 반복할 시간)

    private State currentState = State.Idle;                // 좀비의 현 상태
    private State lastState = State.Idle;                   // 좀비의 직전 상태
    private bool isAware = false;                           // 좀비가 공격받았을때 인지 범위와 관계없이 주인공을 인식할 구분자

    private Animator anim;
    private Rigidbody rb;
    private NavMeshAgent nmAgent;                           // 추적AI
    private bool isPlayerTargeting;                         // 공격시점에 플레이어가 공격 범위에 있는지 판단하기위한 구분자

    PlayerHealth player;

    private AudioSource audioF;
    private AudioSource audioS;
    private AudioSource audioT;
    [Header("Audio Clips")]
    public AudioClip[] footStepClips;
    public AudioClip[] attackClips;
    public AudioClip GrowlVoiceClip, chasingVoiceClip,
        attackVoiceClip, beAttacktedClip,
        beAttackedVoiceClip, dyingVoiceClip;



    void Start()
    {
        HP = 100;
        ap = 30;
        walkSpeed = 100f;
        runSpeed = 6f;
        attackRange = 1.3f;
        normalSearchDistance = 12f;
        awareSearchDistance = 70f;
        normalSearchAngle = 140f;
        awareSearchAngle = 360f;

        currentSearchDistance = normalSearchDistance;
        currentSearchAngle = normalSearchAngle;
        walkDelayMin = 2.0f;
        walkDelayMax = 3.5f;
        idleDelayMin = 1.5f;
        idleDelayMax = 4.0f;

        idleWalkDelay = Random.Range(walkDelayMin, walkDelayMax);
        idleDelay = Random.Range(idleDelayMin, idleDelayMax);
        awareTime = 10.0f;

        thinkIntervalTime = 0.3f;

        anim = GetComponent<Animator>();
        rb = GetComponent<Rigidbody>();
        nmAgent = GetComponent<NavMeshAgent>();

        nmAgent.speed = runSpeed;
        nmAgent.updateRotation = false;

        // 코루틴 시작
        StartCoroutine(Think());

        // 플레이어를 대체할 테스트용 코드
        player = FindObjectOfType<PlayerHealth>();

        audioF = GetComponent<AudioSource>();
        audioS = gameObject.AddComponent<AudioSource>();
        audioT = gameObject.AddComponent<AudioSource>();
    }


    void Update()
    {
        if (isDead) return;                     // 좀비가 죽어있으면 즉시 리턴

        // 속도 이동 
        if (currentState == State.Walk)
            rb.velocity = transform.forward * walkSpeed * Time.deltaTime;

        // 좀비 네비발동시 자연스러운 회전
        if (!nmAgent.isStopped)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(nmAgent.desiredVelocity), Time.deltaTime * 7.0f);
    }

    IEnumerator Think()
    {
        while (!isDead)
        {
            // 공격받았을때 무조건 인지 시간을 초과하면 노멀로 초기화
            if (isAware)
            {
                if (Time.time - timeFlag > awareTime)
                {
                    currentSearchDistance = normalSearchDistance;
                    currentSearchAngle = normalSearchAngle;
                    isAware = false;
                }
            }

            switch (currentState)
            {
                case State.Idle:
                    if (SearchPlayer())
                    {
                        TraceRun();
                        break;
                    }

                    if (Time.time - timeFlag > idleDelay)
                    {
                        WalkAround();
                    }
                    break;
                case State.Walk:
                    if (SearchPlayer())
                    {
                        TraceRun();
                        break;
                    }

                    if (Time.time - timeFlag > idleWalkDelay)
                    {
                        Idle();
                    }
                    break;
                case State.Trace:
                    if (player.isDead)
                    {
                        Idle();
                        break;
                    }

                    if (Vector3.Distance(player.transform.position, transform.position) < attackRange)
                    {
                        Battle();
                    }
                    else if (Vector3.Distance(player.transform.position, transform.position) > currentSearchDistance)
                    {
                        Idle();
                    }
                    else
                    {
                        nmAgent.SetDestination(player.transform.position);
                    }
                    break;
                case State.BattleMode:
                    if (player.isDead)
                    {
                        Idle();
                        break;
                    }

                    if (Vector3.Distance(player.transform.position, transform.position) > attackRange)
                    {
                        TraceRun();
                    }
                    else
                    {
                        Attack();
                    }
                    break;
                case State.Attack:
                    if (player.isDead)
                    {
                        Idle();
                        break;
                    }
                    break;
                case State.Dead:

                    break;
            }

            yield return new WaitForSeconds(thinkIntervalTime);
        }
    }

    void Idle()
    {
        nmAgent.isStopped = true;

        timeFlag = Time.time;
        idleDelay = Random.Range(idleDelayMin, idleDelayMax);
        currentState = State.Idle;

        //audioF.clip = GrowlVoiceClip;
        //audioF.Play();

        if (Random.Range(0, 2) == 0)
        {
            transform.eulerAngles = new Vector3(transform.eulerAngles.x, transform.eulerAngles.y + Random.Range(-90, 0) - 30, transform.eulerAngles.z);
        }
        else
        {
            transform.eulerAngles = new Vector3(transform.eulerAngles.x, transform.eulerAngles.y + Random.Range(0, 90) + 30, transform.eulerAngles.z);
        }

        anim.SetInteger("ZombieState", (int)currentState);
    }

    void WalkAround()
    {
        nmAgent.isStopped = true;
        timeFlag = Time.time;
        idleWalkDelay = Random.Range(walkDelayMin, walkDelayMax);
        currentState = State.Walk;

        anim.SetInteger("ZombieState", (int)currentState);
    }

    void TraceRun()
    {
        nmAgent.isStopped = false;
        currentState = State.Trace;
        anim.SetInteger("ZombieState", (int)currentState);
    }

    void Battle()
    {
        transform.LookAt(player.transform);
        nmAgent.isStopped = true;
        currentState = State.BattleMode;
        anim.SetInteger("ZombieState", (int)currentState);
    }

    void Attack()
    {
        Debug.Log("좀비 어택 범위에 플레이어 들어옴");
        nmAgent.isStopped = true;
        currentState = State.Attack;
        anim.SetInteger("ZombieState", (int)currentState);
        audioS.clip = attackVoiceClip;
        audioS.Play();
    }
    public void AttackPointHandler()
    {
        Debug.Log("좀비 어택 포인트 들어옴");
        if (isPlayerTargeting && !player.isDead)
        {
            Debug.Log("좀비 어택 포인트 성공");
            audioS.clip = attackClips[Random.Range(0, 3)];
            audioS.Play();
            player.OnDamage(ap / 2);
        }
    }
    public void AttackAnimationCompletHandler()
    {
        Battle();
    }

    public void OnDamage(float attackPoint, Vector3 hitPosition)
    {
        isAware = true;
        timeFlag = Time.time;
        currentSearchDistance = awareSearchDistance;
        currentSearchAngle = awareSearchAngle;

        audioF.clip = beAttackedVoiceClip;
        audioF.Play();
        audioT.clip = beAttacktedClip;
        audioT.Play();

        HP -= attackPoint;
        if (HP <= 0)
        {
            HP = 0;
            Die();
        }
    }

    public void Die()
    {
        if (isDead) return;

        nmAgent.isStopped = true;
        currentState = State.Dead;
        anim.SetTrigger("isDead");
        audioS.clip = dyingVoiceClip;
        audioS.Play();
        isDead = true;
        GetComponent<CapsuleCollider>().height = 0.1f;
        GetComponent<CapsuleCollider>().center = new Vector3(0, 0.2f, 0);

        gameObject.layer = 0;

        GameManager.Instance.ZombieDead(name);

        StopCoroutine(Think());
    }

    public void DieAnimationCompletHandler()
    {
        gameObject.SetActive(false);
    }


    bool SearchPlayer()
    {
        RaycastHit[] hits = Physics.SphereCastAll(transform.position, currentSearchDistance, transform.forward, 0);

        foreach (var hit in hits)
        {
            if (hit.collider)
            {
                PlayerHealth hitPlayer = hit.collider.GetComponent<PlayerHealth>();

                // 플레이어가 존재하고, 좀비의 시야각 안에 있으며, 탐색거리 안에 있다면 
                if (hitPlayer
                    && Vector3.Angle(Vector3.forward, transform.InverseTransformPoint(hitPlayer.transform.position)) < currentSearchAngle / 2.0f
                    && Vector3.Distance(hitPlayer.transform.position, transform.position) < currentSearchDistance)
                {
                    // 주인공에게 피격당했을시 무조건 인식
                    if (isAware)
                    {
                        return true;
                    }

                    RaycastHit rHit;
                    Vector3 midPoint = new Vector3(0, 0.8f, 0);      // 몸의 중간 포인트로 조준
                    // 좀비에서 플레이어방향으로 레이캐스트 실행
                    if (Physics.Linecast(transform.position + midPoint, hitPlayer.transform.position + midPoint, out rHit, LayerMask.GetMask("Player")))
                    {
                        // 직선방향에 검출된 대상이 플레이어라면 (둘 사이에 존재하는 다른 콜라이더가 없음을 의미)
                        // 플레이어의 위치로만 검출하므로 정교한 방법은 아님 %% 주의 (임시방편)
                        if (rHit.transform.CompareTag("Player"))
                        {
                            return true;
                        }
                    }

                }
            }
        }
        return false;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.tag == "Player")
        {
            Debug.Log("좀비 어택 범위에 플레이어 들어옴");
            isPlayerTargeting = true;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.tag == "Player")
        {
            Debug.Log("좀비 어택 범위에서 플레이어 나감");
            isPlayerTargeting = false;
        }
    }

    public void FootStepSound()
    {
        audioS.clip = footStepClips[Random.Range(0,3)];
        audioS.Play();
    }


    public void ChasingVoiceSound()
    {
        audioT.clip = chasingVoiceClip;
        audioT.Play();
    }
}