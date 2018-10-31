//
// 고정된 크기의 맵을 로딩할 때 난이도 조절 문제와 일관된 배경 및 프랍을 사용할 수 밖에 없는
// 문제점 때문에 셋팅한 환경 및 주어진 전체 블록을 사용해 맵을 자동으로 랜덤하게 생성하는
// 기능을 개발하였습니다.
//
// 아래는 Single 모드일 때의 맵 생성 및 삭제를 관리하는 코드입니다.
// 이후 작성 되었던 Multi-play 모드일 때의 맵을 관리하는 코드는 퇴사 이후 많은 개편이 이루어져
// 직접 작성했던 코드의 대부분이 유실되었습니다.
//

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public class SingleMapManager : MapManager
{
    //     part       0                                                          1                ...
    //  bg(subPart)   0                    450                     900           1350             ...
    //     block      0 75 150 225 300 375 450 525 600 675 750 825 900         ....

    // 생성되는 Part들이 각각 포함하는 SubPartData를 가지고 있다.
    // 서브파트는 실제 생성된(될) 배경과 블록들을 포함한다.
    public class PartData
    {
        // 파트의 시작과 끝 위치는 subPartData[begin]의 bgPos와 subPartData[end]의 bgPos + 450이다.
        public Part part;
        public List<SubPartData> subPartData;

        public PartData()
        {
            subPartData = new List<SubPartData>();
        }
    }

    // BG & Block Queue
    public class SubPartData
    {
        public Part part;   // 현재 part
        public int bgSequence; // 어떤 모양의 배경인 지 (식별자)
        public int bgCount; // 몇 번째로 생성된 배경인 지
        public int bgID;    // subPart에 포함된 bg의 ID
        public int bgPos;   // 맵 상 위치. 1 = 75(z)

        public List<BlockData> blocks;  // subPart에 포함된 block
        public List<int> blocksPos;

        public SubPartData()
        {
            blocks = new List<BlockData>();
            blocksPos = new List<int>();
        }
    }



    #region Object Pooling Classes
    public class BGPool
    {
        public int id;      // 식별을 위한 ID
        public Transform bgInst;
        public Part part;   // 어느 파트인 지
        public int bgSequence; // Inspector에서 등록된 배경의 순서(3개 중 첫번째면 0)
        public bool isUse;  // 현재 사용 중이면 true
        public bool isQueued; // SubPartData에 입력이 된 상태면 true

        // 오브젝트 사용을 끝내고 Pool에 넣는다.
        public void Push()
        {
            bgInst.transform.position = poolPos;
            bgInst.gameObject.SetActive(false);
            isUse = false;
            bgInst.parent = bgPoolParent.transform;
        }
        // 오브젝트를 사용하기 위해 Pool에서 꺼낸다.
        public void Pull(int pos)
        {
            bgInst.transform.position = new Vector3(0f, 0f, pos * 75 + initPosZ);
            bgInst.gameObject.SetActive(true);
            isUse = true;
            isQueued = false;
            bgInst.parent = bgPoolParent.transform.parent;
        }
    }

    public class BlockPool
    {
        public int id;
        public BlockData blockData;
        public Part part;
        public bool isUse;
        
        public void Push()
        {
            blockData.block.transform.position = poolPos;
            blockData.block.gameObject.SetActive(false);
            isUse = false;
            blockData.block.parent = blockPoolParent.transform;
        }
        public void Pull(int pos)
        {
            blockData.block.transform.position = new Vector3(0f, 0f, pos * 75 + initPosZ);
            blockData.block.gameObject.SetActive(true);
            isUse = true;
            blockData.block.parent = blockPoolParent.transform.parent;
        }
    }

    public class MapPool
    {
        public List<BGPool> bgPool;
        public List<BlockPool> blockPool;

        public MapPool()
        {
            bgPool = new List<BGPool>();
            blockPool = new List<BlockPool>();
            bgPoolParent = new GameObject("bgPoolParent");
            blockPoolParent = new GameObject("blockPoolParent");
        }
    }
    #endregion

    private static GameObject bgPoolParent;
    private static GameObject blockPoolParent;
    private static Vector3 poolPos = new Vector3(0f, 0f, -100f);

    private readonly int MAX_BG_COUNT = 7;   // 한 파트당 배경의 최대 길이
    private int PART_COUNT;                  // 현재 맵의 파트 개수
    private int GRADE_UP_DISTANCE = 2000;    // 플레이어가 z값 2000의 배수에 도달할 때마다 난이도 변경 (z값 4 = 1m)

    private int removedBlockCount = 0;           // 현재까지 지워진 블록 개수 (누적)
    private int totalBlockCount = 0;             // 현재 파트까지 실제 생성될 블럭 총 개수 (누적 X)

    private bool isInitMap;                  // 최초 맵이 초기화 되었는지 확인

    private List<PartData> partData;
    private MapPool mapPool;
    private List<SpecialBlock> specialBlockList;
    private InGameRecoverObjSpawner recoverobjSpawner;
    private InGameMonsterSpawner monsterSpawner;

    void Awake()
    {
        partData = new List<PartData>();
        mapPool = new MapPool();
        specialBlockList = new List<SpecialBlock>();
        bgCount = 0;
        isInitMap = false;

        PhotonProtocol.Instance.GetRandomMapId((res) => GetRandomMapIdSuccessCallback(res), (err) => { });
    }

    private void GetRandomMapIdSuccessCallback(Response<object> response)
    {
        GetRandomMapIdResult result = (GetRandomMapIdResult)response.result;

        SingleBGControl.instance.SetBGListByMap(result.mapId);

        delayTime = 0.2f;
        createdBlockCount = 0;
        initPosZ = 150;

        curMapData = mapDataList.FirstOrDefault(x => x.mapId == result.mapId);
        PART_COUNT = curMapData.partSequence.Length;

        // 맵 옵션 설정
        int randNum = Random.Range(0, curMapData.option.Count);
        MapOptionSetting(result.mapId, randNum);

        // 블록 받아오기
        blockList = new List<List<Transform>>();
        blockList.Add(curMapData.planeBlockList);
        blockList.Add(curMapData.hillBlockList);
        blockList.Add(curMapData.gapBlockList);
        blockList.Add(curMapData.wallBlockList);
        blockList.Add(curMapData.cliffBlockList);
        blockList.Add(curMapData.earthquakeBlockList);
        blockList.Add(curMapData.valleyBlockList);

        // 초기 Grade 설정 및 적용
        curGrade = GradeType.VERYEASY;
        SetGradeInfo(curGrade);

        //-> 파트별 블럭 리스트 초기화
        for (int i = 0; i < curMapData.partSequence.Length; i++)
            blockInEachPart.Add(new List<Transform>());

        // 파트의 크기에 맞게 PartData 생성
        for (int i = 0; i < PART_COUNT; i++)
            partData.Add(new PartData());

        // 1. 배경 오브젝트 풀 초기화 & 배경 7개씩 생성
        CreateBGInstances();

        // 2. 각 파트별 길이 초기화
        InitAllPartLength();

        // 3. 배경을 선택, 파트별 배경 리스트 중 순서를 어떻게 할 지를 결정
        for (int i = 0; i < PART_COUNT; i++)
            QueueBGInstToSubPart((Part)i);

        // '블록 생성 확률' 배열 초기화
        curGradeData.arrayBlockRate = new int[] { curGradeData.ratePlane, curGradeData.rateHill, curGradeData.rateGap,
            curGradeData.rateWall, curGradeData.rateCliff, curGradeData.rateEarthquake, curGradeData.rateValley };

        // '블록 개수' 배열 초기화
        curMapData.eachBlockCount = new int[] { curMapData.numPlane, curMapData.numHill, curMapData.numGap,
            curMapData.numWall, curMapData.numCliff, curMapData.numEarthquake, curMapData.numValley };

        InitBlockCount();

        // startBlock 추가
        BlockData startBlockData = new BlockData(curMapData.startBlock, -2, 0, 0f);
        GenerateBlock(startBlockData, new Vector3(0, 0, 0));

        // 블록 생성
        StartCoroutine(GenerateMap(-1));
    }

    // 배경을 선택, 파트별 배경 리스트 중 순서를 결정
    // 선택되었으면 partData -> subPartData에 저장한다.
    private void QueueBGInstToSubPart(Part part)
    {
        int bgSeq;
        int bgSeq_temp; // bgSeq를 다시 뽑을 때 사용되는 임시 변수

        if (string.Equals(curMapData.mapName, "Tokyo"))
        {
            for (int i = 0; i < curMapData.partBGCount[(int)part]; i++)  // 파트 내 생성될 BG 개수만큼
            {
                // 몇 번째 배경을 뽑을 지 결정 (bgSequence)
                bgSeq = Random.Range(0, SingleBGControl.instance.curBG.bgListByPart[(int)part].bgByPart.Count);

                if (Equals(part, Part.A))
                {
                    if (bgSeq == 0)
                    {
                        SpecialBlock sb = new SpecialBlock();
                        sb.block = curMapData.specialBlockData[0].block; // 0번은 Tokyo 전용(plane_non_gimmick)
                        sb.part = part;
                        sb.pos = bgPos + 3;
                        specialBlockList.Add(sb);
                    }
                    if (i == curMapData.partBGCount[(int)part] - 1) // 맨 끝에는 파트 B에 대비해 non_gimmick을 2개 만든다
                    {
                        for (int j = 0; j < 2; j++)
                        {
                            SpecialBlock sb = new SpecialBlock();
                            sb.block = curMapData.specialBlockData[0].block; // 0번은 Tokyo 전용(plane_non_gimmick)
                            sb.part = part;
                            sb.pos = bgPos + j + 4;
                            specialBlockList.Add(sb);
                        }
                    }
                }
                else if (Equals(part, Part.B))
                {
                    if (i == 0)
                    {
                        bgSeq = 0;  // tunnel_start

                        SpecialBlock sb = new SpecialBlock();
                        sb.block = curMapData.specialBlockData[0].block; // 0번은 Tokyo 전용(plane_non_gimmick)
                        sb.part = part;
                        sb.pos = bgPos;
                        specialBlockList.Add(sb);
                    }
                    else if (i == curMapData.partBGCount[(int)part] - 1)
                    {
                        bgSeq = 2;  // tunnel_end

                        for (int j = 0; j < 2; j++)
                        {
                            SpecialBlock sb = new SpecialBlock();
                            sb.block = curMapData.specialBlockData[0].block;
                            sb.part = part;
                            sb.pos = bgPos + j + 5;  // bg시작 좌표 + 375
                            specialBlockList.Add(sb);
                        }
                    }
                    else
                    {
                        bgSeq = 1;  // tunnel_mid
                    }
                }

                // 뽑을 배경 리스트 입력
                BGPool bgPool = null;

                while (true)
                {
                    for (int j = 0; j < MAX_BG_COUNT; j++)
                    {
                        // bgPool에 등록된 배경 중 종류(bgSeq, part)가 같고, SubPartData에 넣지 않은(!isQueued) 배경을 찾는다.
                        bgPool = mapPool.bgPool.Find(x => x.bgSequence == bgSeq && x.part == part && x.isQueued == false);   // id도 체크 필요

                        if (bgPool != null)
                        {
                            bgPool.isQueued = true; // queue 되었음 체크
                            break;
                        }
                    }

                    // BG를 생성하려 하는데 pool에 없을 경우 다른 BG를 뽑기 위해 다시 랜덤값 생성
                    if (bgPool == null)
                    {
                        bgSeq_temp = bgSeq;
                        while (true)
                        {
                            bgSeq = Random.Range(0, SingleBGControl.instance.curBG.bgListByPart[(int)part].bgByPart.Count);
                            if (bgSeq == bgSeq_temp)
                                continue;
                            else
                                break;
                        }
                    }
                    else
                    {
                        break;
                    }
                }

                SubPartData subPartData = new SubPartData();
                subPartData.bgSequence = bgPool.bgSequence;
                subPartData.bgID = bgPool.id;
                subPartData.part = part;
                subPartData.bgCount = bgCount;
                subPartData.bgPos = bgPos;

                partData[(int)part].subPartData.Add(subPartData);

                bgCount++;
                bgPos = bgPos + 6;
            }
        }
    }

    // subPartData에 들어간 걸 빼내어 배치함
    private void DequeueBGInstFromSubPart(int pos)
    {
        // 현재 위치에 나올 배경을 Queue에서 찾는다.
        SubPartData spData = partData[(int)curPart].subPartData.Find(x => x.bgPos == pos);

        // Queue에서 찾은 배경과 part, 종류가 같고, 사용중이지 않은 배경을 Pool에서 꺼내어 배치한다.
        mapPool.bgPool.Find(x => x.bgSequence == spData.bgSequence && x.part == spData.part && x.isQueued == true).Pull(pos);

    }

    // 배경 생성, Pool에 Push
    private void CreateBGInstances()
    {
        BGPool bgPool;
        Vector3 initPos = new Vector3(0f, 0f, -100f);  // temporal pos
        int id = 0;

        for (int i = 0; i < PART_COUNT; i++)
        {
            SingleBGControl.instance.Initialize();

            for (int j = 0; j < MAX_BG_COUNT; j++)
            {
                bgPool = new BGPool();
                bgPool.id = id;
                bgPool.part = curMapData.partSequence[i];
                bgPool.bgSequence = SingleBGControl.instance.bgSequence;
                bgPool.bgInst = SingleBGControl.instance.GenerateBG(initPos, curMapData.partSequence[i], curMapData.mapName);
                bgPool.Push();

                mapPool.bgPool.Add(bgPool);
                id++;
            }
        }
    }

    // 전체 파트의 길이를 초기화한다.
    private void InitAllPartLength()
    {
        // Init to 0
        for (int i = 0; i < PART_COUNT; i++)
        {
            curMapData.partBGCount[i] = 0;
            curMapData.partPos[i] = 0;
            curMapData.accumulatedBlockCount = 0;
        }

        // Set value
        for (int i = 0; i < PART_COUNT; i++)
        {
            int length = Random.Range(3, 8);
            curMapData.partBGCount[i] = length;
            curMapData.partPos[i] = curMapData.accumulatedBlockCount + (length * 6);
            curMapData.accumulatedBlockCount = curMapData.partPos[i];
            totalBlockCount = curMapData.accumulatedBlockCount;
        }
    }

    // 특정 파트의 길이를 초기화한다.
    private void InitPartLength(Part part)
    {
        int length = Random.Range(3, 8);

        curMapData.partBGCount[(int)part] = length;
        curMapData.partPos[(int)part] = curMapData.accumulatedBlockCount + (length * 6);
        curMapData.accumulatedBlockCount = curMapData.partPos[(int)part];
        totalBlockCount = curMapData.accumulatedBlockCount - removedBlockCount; // 누적 블록개수에서 지워진 블록개수 뺌
    }

    // Grade 정보 셋팅
    private void SetGradeInfo(GradeType grade)
    {
        curGradeData = gradeDataList.FirstOrDefault(list => list.grade == grade);  // 현재 Grade에 맞는 GradeData 로드

        // '블록 생성 확률' 배열 초기화
        curGradeData.arrayBlockRate = new int[] { curGradeData.ratePlane, curGradeData.rateHill, curGradeData.rateGap,
            curGradeData.rateWall, curGradeData.rateCliff, curGradeData.rateEarthquake, curGradeData.rateValley };

        // '블록 개수' 배열 초기화
        curMapData.eachBlockCount = new int[] { curMapData.numPlane, curMapData.numHill, curMapData.numGap,
            curMapData.numWall, curMapData.numCliff, curMapData.numEarthquake, curMapData.numValley };
    }

    // part 파라미터는 제거된 특정 파트를 지정해 pool에서 꺼내기 위함
    private IEnumerator GenerateMap(int part)
    {
        GetCurrentPart();

        BlockData blockData;
        WaitForSeconds delay = new WaitForSeconds(delayTime);
        int loopCount;

        if (!isInitMap)  // 최초 맵 생성 시
            loopCount = curMapData.accumulatedBlockCount;
        else             // Reconstruction 시
            loopCount = curMapData.partPos[part];

        for (int i = 0; i < loopCount; i++)
        {
            yield return delay;
            GetCurrentPart();

            int sbNum = CheckSpecialBlockPos();
            if (sbNum != -1) // -1이 아닐 경우 special block 생성
            {
                blockData = new BlockData(specialBlockList[sbNum].block);
                SetBlockPos(blockData);
            }
            else
            {
                blockData = GetRandomBlock();
                if (blockData != null)
                    SetBlockPos(blockData);
            }

            GenerateBackGroundInst(curMapData.accumulatedBlockCount); //누적
            createdBlockCount++;
            CheckRunDistance();

            if (createdBlockCount == curMapData.accumulatedBlockCount)
                break;
        }

        if (!isInitMap)
        {
            isInitMap = true;
            StartCoroutine(ReconstructMap());
        }

        // Map Objects를 만들기 위한 코드
        monsterSpawner = RunCoreSpawn.monsterSpawner.GetComponent<InGameMonsterSpawner>();
        monsterSpawner.GetBlockList(blockInEachPart);
        monsterSpawner.SpawnInGameMonster();
    }

    private void SetBlockPos(BlockData blockData, int size = 1)
    {
        Vector3 anchorPos;

        anchorPos = new Vector3(0, blockData.block.position.y, (createdBlockCount * 75) + initPosZ);
        GenerateBlock(blockData, anchorPos);
    }

    private void GenerateBlock(BlockData blockData, Vector3 position)
    {
        BlockPool blockPool;

        if (!isInitMap)  // 초기 맵 생성 시
        {
            string path = PhotonPrefabPathContainer.GetPrefabPath(blockData.block.name);
            Transform newBlock = PhotonNetwork.InstantiateSceneObject(path, position, Quaternion.identity, 0, null).transform;

            blockData.block = newBlock;
            blockData.endHeight = newBlock.GetComponent<Block>().endHeight;

            if (blockData.type != -2) // startBlock이 아니면
            {
                // 블럭을 현재 파트 블럭리스트에 추가
                blockInEachPart[(int)curPart].Add(newBlock);

                // 블록 텍스쳐 변경을 위해 블록에 파트값 부여
                blockData.block.GetComponent<Block>().part = curPart;
                blockData.block.GetComponent<ChangeBlockTexture>().SetBlockTexture((int)curPart);

                // input into pool
                blockPool = new BlockPool();
                blockPool.blockData = blockData;
                blockPool.part = curPart;
                blockPool.isUse = true;
                mapPool.blockPool.Add(blockPool);
            }
        }
        else
        {
            // 풀에서 있는 지 검사하고 있다면 꺼내고, 없다면 생성
            blockPool = mapPool.blockPool.Find(x => x.blockData.num == blockData.num && x.blockData.type == blockData.type && x.isUse == false);

            if (blockPool != null)
            {
                blockPool.part = curPart;
                blockPool.blockData.block.GetComponent<Block>().part = curPart;
                blockPool.blockData.block.GetComponent<ChangeBlockTexture>().SetBlockTexture((int)curPart);
                blockPool.Pull(createdBlockCount);
            }
            else
            {
                // Block 및 BlockPool 생성, 현재 blockData를 Add
                string path = PhotonPrefabPathContainer.GetPrefabPath(blockData.block.name);
                Transform newBlock = PhotonNetwork.InstantiateSceneObject(path, position, Quaternion.identity, 0, null).transform;
                blockData.block = newBlock;
                blockData.endHeight = newBlock.GetComponent<Block>().endHeight;

                blockPool = new BlockPool();
                blockPool.part = curPart;
                blockPool.blockData = blockData;
                blockPool.blockData.block.GetComponent<Block>().part = curPart;
                blockPool.blockData.block.GetComponent<ChangeBlockTexture>().SetBlockTexture((int)curPart);
                mapPool.blockPool.Add(blockPool);

                blockPool.Pull(createdBlockCount);
            }
        }

        // listBlockData가 계속 쌓이는 것을 방지
        listBlockData.Add(blockData);
        if (listBlockData.Count > 5)
            listBlockData.RemoveAt(0);

        // subPartData에 추가. SubPartData의 blocks list 개수가 6개가 아니면 추가
        if (blockData.type != -2) // startBlock이 아니면
        {
            SubPartData spData = partData[(int)curPart].subPartData.Find(x => x.part == curPart && x.blocks.Count != 6);
            spData.blocks.Add(blockData);
            spData.blocksPos.Add(createdBlockCount);
        }
    }


    // 블럭을 생성하기 전 현재 파트 체크
    private void GetCurrentPart()
    {
        for (int i = 0; i < PART_COUNT; i++)
        {
            if (createdBlockCount + 1 <= curMapData.partPos[i])
            {
                curPart = curMapData.partSequence[i];
                return;
            }
            continue;
        }

        Debug.LogError("GetCurrentPart Error");
    }

    // special 블록이 나올 위치인가를 체크
    private int CheckSpecialBlockPos()
    {
        for (int i = 0; i < specialBlockList.Count; i++)
        {
            if (createdBlockCount == specialBlockList[i].pos)
                return i;
        }
        return -1;
    }

    private IEnumerator ReconstructMap()
    {
        WaitForSeconds delay = new WaitForSeconds(2f);
        float playerPos;

        // 지나간 파트를 풀에 넣고, SubPartData에서 지운 후, 다시 해당 파트를 뽑아서(길이 초기화->Queueing) SubPartData에 넣고,
        // 풀에서 점차적으로 꺼낸다
        while (true)
        {
            playerPos = RunCoreSpawn.myPlayer.transform.position.z - 200;
            int removePart = -1;

            // 플레이어 좌표보다 뒤에 있는 파트(파트의 마지막 bg + 450)를 찾는다
            for (int i = 0; i < PART_COUNT; i++)
            {
                if ((partData[i].subPartData[partData[i].subPartData.Count - 1].bgPos * 75) + 450 < playerPos)
                {
                    removePart = i;
                    break;
                }
            }

            // 배경 제거. bgPool에 있는 isUse == true인 해당 파트 배경 모두 넣고, 동시에 subPartData 해제
            if (removePart != -1)
            {
                BlockPool blockPool;

                // push bg
                for (int i = 0; i < partData[removePart].subPartData.Count; i++)
                {
                    mapPool.bgPool.Find(x => x.part == (Part)removePart && x.isUse == true).Push();
                }
                // push blocks
                for (int i = 0; i < mapPool.blockPool.Count; i++)
                {
                    blockPool = mapPool.blockPool.Find(x => x.part == (Part)removePart && x.isUse == true);
                    if (blockPool != null)
                    {
                        blockPool.Push();
                        removedBlockCount++;
                    }
                }
                // specialBlockData 제거
                for (int i = 0; i < specialBlockList.Count; i++)
                {
                    if (specialBlockList[i].part == (Part)removePart)
                        specialBlockList.RemoveAt(i);
                }

                partData[removePart].subPartData.Clear();

                // 배경 배치를 위한 Set
                InitPartLength((Part)removePart);
                QueueBGInstToSubPart((Part)removePart);
                StartCoroutine(GenerateMap(removePart));
            }

            yield return delay;
        }
    }

    // 맵이 생성된 위치 기반, 500m (z 2000)마다 난이도를 변경한다
    private void CheckRunDistance()
    {
        // 거리 측정
        int gradeByDist = (createdBlockCount * 75) / GRADE_UP_DISTANCE;

        if ((gradeByDist > (int)curGrade - 1) && curGrade != GradeType.VERYHARD)
        {
            // grade 설정
            curGrade++;
            SetGradeInfo(curGrade);
            InitBlockCount();
        }
    }

    // Inspector에 입력한 비율에 따라 남은 블록 카운트를 초기화
    private void InitBlockCount()
    {
        float blockCount;

        for (int i = 0; i < curGradeData.arrayBlockRate.Length; i++)
        {
            blockCount = curGradeData.arrayBlockRate[i] / 100f * ((partData[(int)curPart].subPartData.Count + 1) * 6); // 현재 파트 블럭 수 + 6
            curMapData.eachBlockCount[i] = (int)blockCount;
        }
    }

    private void GenerateBackGroundInst(int maxBlockCount)
    {
        if (createdBlockCount < maxBlockCount)
        {
            if ((createdBlockCount % 6).Equals(0))
            {
                DequeueBGInstFromSubPart(createdBlockCount);
            }
        }
    }

}