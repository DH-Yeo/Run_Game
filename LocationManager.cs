#pragma warning disable CS0618

using UnityEngine;
using System.Collections.Generic;
using System.Collections;

public class LocationManager : MonoBehaviour {

    public static bool isEnable = false;

    public GPSManager gpsManager;      // GPS 모듈을 관리하고 좌표를 받아오는 클래스
    public GyroManager gyroManager;    // Gyro 센서 데이터 모듈 관리 및 데이터를 받아오는 클래스
    
    [SerializeField] private GameObject cam;
    [SerializeField] private GameObject objectSpawner;  // 생성된 GPS Object들의 부모 오브젝트

    private GetGPSObjectListResult getGPSObjectListResult = null;    // 서버로부터 받아온 GPS Object 리스트
    private List<GameObject> listGPSObject = new List<GameObject>(); // 현재 클래스에서 관리하는 GPS Object 리스트

    private float lat, lon; // 위도 경도
    private float heading;  // 북쪽에 대한 각도
       
	void Update ()
    {
        if (Input.location.status != LocationServiceStatus.Running)
            return;

        if (getGPSObjectListResult == null)
            return;

        // GPS 모드를 껐을 때 로드된 GPS Object들을 잠시 Despawn
        if (!isEnable)
        {
            DespawnObjects();
            return;
        }

        // 북쪽의 각도를 Gyro 센서로부터 받아온다
        heading = gyroManager.Heading;
        
        // 자이로 센서에 의한 scene의 카메라 각도 업데이트
        cam.transform.eulerAngles = new Vector3(gyroManager.RotationX, gyroManager.RotationY - 180f, 180f);
                
        // 생성된 오브젝트들 이동 (플레이어가 기준, 이동할 때)
        StartCoroutine("MoveObjects");
    }


    // 서버로부터 받은 현재 위치 주변의 gpsObject 데이터를 기반으로 테이블 데이터와 매칭하여 오브젝트들을 생성한다.
    public void CreateGPSObject(GetGPSObjectListResult result)
    {
        getGPSObjectListResult = result;

        GPSObjectTable goTable = new GPSObjectTable();

        for (int i = 0; i < result.gpsObjectList.Count; i++)
        {
            // Table과 매칭시켜 i 번째 gpsObject의 정보를 받아온다
            goTable = TableManager.Instance.gpsObjectTable.Find(t => t.id == result.gpsObjectList[i].id);

            // 해당되는 prefab을 불러와 scene에 생성한다
            System.Text.StringBuilder sb = new System.Text.StringBuilder("prefab/AR/GPS/");
            GameObject go = Instantiate(Resources.Load(sb.Append(goTable.model).ToString())) as GameObject;

            // 현재 gpsObject의 정보 입력
            GPSObject gpsObj = go.GetComponent<GPSObject>();
            gpsObj.id = result.gpsObjectList[i].id;
            gpsObj.type = goTable.type;
            gpsObj.lat = result.gpsObjectList[i].lat;
            gpsObj.lon = result.gpsObjectList[i].lon;
            
            // 현재 유저의 위치로부터 gpsObject의 거리를 화면에 표시할 Text 오브젝트 생성
            GameObject goText = Instantiate(Resources.Load("prefab/AR/GPS/DistanceText")) as GameObject;
            gpsObj.distText = goText.GetComponent<TextMesh>();
            gpsObj.distText.transform.localPosition += new Vector3(0f, -0.08f, 0f);  // initialize text position

            // gpsObject와 text를 묶어줄 부모 객체 생성
            gpsObj.objParent = new GameObject();
            System.Text.StringBuilder nameSb = new System.Text.StringBuilder("GPSObject_");
            gpsObj.objParent.name = nameSb.Append(gpsObj.id).ToString();
            
            gpsObj.objParent.transform.SetParent(objectSpawner.transform);
            go.transform.parent = gpsObj.objParent.transform;
            gpsObj.distText.transform.parent = gpsObj.objParent.transform;

            gpsObj.objParent.SetActive(false);

            // 리스트에 추가, 이후 MoveObjects()에서 관리
            listGPSObject.Add(go);
        }
    }

    private IEnumerator MoveObjects()
    {
        yield return new WaitForSeconds(0.3f);  // for avoiding frequent updating

        GPSObject gpsObj;

        for (int i = 0; i < listGPSObject.Count; i++)
        {
            // 현재 유저와 실제 gpsObject의 거리를 계산하여 meter 단위의 값을 저장
            float x = 0f, y = 0f;
            gpsObj = listGPSObject[i].GetComponent<GPSObject>();
            gpsObj.distance = (int)CalculateDistance(gpsObj.lat, gpsObj.lon, ref x, ref y);

            // 일정 거리 이상 멀어지면 SetActive를 끄거나 켠다.
            if (200f <= gpsObj.distance)
            {
                gpsObj.objParent.SetActive(false);
                continue;
            }
            else
            {
                gpsObj.objParent.SetActive(true);
            }

            // 좌표계 중 고도는 포함되어 있지 않아 유저가 보기 편하도록
            // 오브젝트의 거리가 멀리 있을 수록 조금 높이 있는 것처럼 표시하기 위한 변수
            float height = 0f;
            height = (gpsObj.distance * 0.05f) - 2f;

            // lerp를 이용한 gpsObject의 이동
            // 실제 유저의 위치에 따라 Scene에서는 유저의 위치를 기준으로 gpsObject들이 이동하며, 크기가 변한다.
            gpsObj.objParent.transform.localPosition = Vector3.Lerp(gpsObj.objParent.transform.localPosition,
                                                            new Vector3((x * 0.5f), height, (y * 0.5f)), Time.deltaTime * 20);

            // 거리에 따른 scale 변환. 멀리 있을 수록 크기가 작아진다
            float objScale = GetScaleFromDistance(gpsObj.distance);
            gpsObj.objParent.transform.localScale = Vector3.Lerp(gpsObj.objParent.transform.localScale,
                                                                                new Vector3(objScale, objScale, 0), Time.deltaTime * 20);

            // gpsObject는 2D 이미지를 사용했기 때문에 모든 오브젝트가 카메라를 바라보도록 회전시킨다
            gpsObj.objParent.transform.LookAt(cam.transform);
            gpsObj.objParent.transform.Rotate(new Vector3(0, 180, 0), Space.World);

            gpsObj.distText.text = gpsObj.distance.ToString() + "m";
        }
    }

    // GPS 모드를 끌 때 잠시 gpsObject들을 despawn 시키는 함수
    private void DespawnObjects()
    {
        for (int i = 0; i < listGPSObject.Count; i++)
        {
            GPSObject gpsObj = listGPSObject[i].GetComponent<GPSObject>();
            gpsObj.objParent.SetActive(false);
        }
    }

    // 두 지점 간 직선거리 x, y 계산
    private float CalculateDistance(float newLat, float newLon, ref float x, ref float y)
    {
        Vector3 position = Vector3.zero;

        // 두 지점의 실제 좌표에 대한 거리를 계산하는 함수. (계산 공식이 어려워 구글링을 통해 가져옴)
        geodeticOffsetInv(lat * Mathf.Deg2Rad, lon * Mathf.Deg2Rad, newLat * Mathf.Deg2Rad, newLon * Mathf.Deg2Rad,
                            out x, out y);

        return new Vector3(x, 0, y).magnitude;
    }

    // spawn 된 오브젝트의 거리에 따른 scale 조절
    float GetScaleFromDistance(float dist)
    {
        float scale = 0;

        if (dist >= 190) { scale = (dist * 0.06f); }
        else if (dist >= 180) { scale = (dist * 0.07f); }
        else if (dist >= 170) { scale = (dist * 0.08f); }
        else if (dist >= 160) { scale = (dist * 0.09f); }
        else if (dist >= 150) { scale = (dist * 0.1f); }
        else if (dist >= 140) { scale = (dist * 0.13f); }
        else if (dist >= 130) { scale = (dist * 0.17f); }
        else if (dist >= 120) { scale = (dist * 0.2f); }
        else if (dist >= 110) { scale = (dist * 0.24f); }
        else if (dist >= 100) { scale = (dist * 0.28f); }
        else if (dist >= 90) { scale = (dist * 0.33f); }
        else if (dist >= 80) { scale = (dist * 0.38f); }
        else if (dist >= 70) { scale = (dist * 0.43f); }
        else if (dist >= 60) { scale = (dist * 0.49f); }
        else if (dist >= 50) { scale = (dist * 0.53f); }
        else if (dist >= 40) { scale = (dist * 0.59f); }
        else if (dist >= 30) { scale = (dist * 0.63f); }
        else if (dist >= 20) { scale = (dist * 0.68f); }
        else if (dist >= 10) { scale = (dist * 0.72f); }
        else { scale = (dist * 0.74f); }
        
        return scale;
    }


    #region calculate x, y distance
    //for calculate coordinates to distance
    private float GD_semiMajorAxis = 6378137.000000f;
    private float GD_TranMercB = 6356752.314245f;
    private float GD_geocentF = 0.003352810664f;

    private void geodeticOffsetInv(float refLat, float refLon, float lat, float lon,
                            out float xOffset, out float yOffset)
    {
        float a = GD_semiMajorAxis;
        float b = GD_TranMercB;
        float f = GD_geocentF;

        float L = lon - refLon;
        float U1 = Mathf.Atan((1 - f) * Mathf.Tan(refLat));
        float U2 = Mathf.Atan((1 - f) * Mathf.Tan(lat));
        float sinU1 = Mathf.Sin(U1);
        float cosU1 = Mathf.Cos(U1);
        float sinU2 = Mathf.Sin(U2);
        float cosU2 = Mathf.Cos(U2);

        float lambda = L;
        float lambdaP;
        float sinSigma;
        float sigma;
        float cosSigma;
        float cosSqAlpha;
        float cos2SigmaM;
        float sinLambda;
        float cosLambda;
        float sinAlpha;
        int iterLimit = 100;
        do
        {
            sinLambda = Mathf.Sin(lambda);
            cosLambda = Mathf.Cos(lambda);
            sinSigma = Mathf.Sqrt((cosU2 * sinLambda) * (cosU2 * sinLambda) +
                (cosU1 * sinU2 - sinU1 * cosU2 * cosLambda) *
                (cosU1 * sinU2 - sinU1 * cosU2 * cosLambda));
            if (sinSigma == 0)
            {
                xOffset = 0.0f;
                yOffset = 0.0f;
                return;  // co-incident points
            }
            cosSigma = sinU1 * sinU2 + cosU1 * cosU2 * cosLambda;
            sigma = Mathf.Atan2(sinSigma, cosSigma);
            sinAlpha = cosU1 * cosU2 * sinLambda / sinSigma;
            cosSqAlpha = 1 - sinAlpha * sinAlpha;
            cos2SigmaM = cosSigma - 2 * sinU1 * sinU2 / cosSqAlpha;
            if (cos2SigmaM != cos2SigmaM) //isNaN
            {
                cos2SigmaM = 0;  // equatorial line: cosSqAlpha=0 (§6)
            }
            float C = f / 16 * cosSqAlpha * (4 + f * (4 - 3 * cosSqAlpha));
            lambdaP = lambda;
            lambda = L + (1 - C) * f * sinAlpha *
                (sigma + C * sinSigma * (cos2SigmaM + C * cosSigma * (-1 + 2 * cos2SigmaM * cos2SigmaM)));
        } while (Mathf.Abs(lambda - lambdaP) > 1e-12 && --iterLimit > 0);

        if (iterLimit == 0)
        {
            xOffset = 0.0f;
            yOffset = 0.0f;
            return;  // formula failed to converge
        }

        float uSq = cosSqAlpha * (a * a - b * b) / (b * b);
        float A = 1 + uSq / 16384 * (4096 + uSq * (-768 + uSq * (320 - 175 * uSq)));
        float B = uSq / 1024 * (256 + uSq * (-128 + uSq * (74 - 47 * uSq)));
        float deltaSigma = B * sinSigma * (cos2SigmaM + B / 4 * (cosSigma * (-1 + 2 * cos2SigmaM * cos2SigmaM) -
            B / 6 * cos2SigmaM * (-3 + 4 * sinSigma * sinSigma) * (-3 + 4 * cos2SigmaM * cos2SigmaM)));
        float s = b * A * (sigma - deltaSigma);

        float bearing = Mathf.Atan2(cosU2 * sinLambda, cosU1 * sinU2 - sinU1 * cosU2 * cosLambda);
        xOffset = Mathf.Sin(bearing) * s;
        yOffset = Mathf.Cos(bearing) * s;
    }
    #endregion
    

}
