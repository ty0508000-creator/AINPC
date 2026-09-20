using System.IO;
using UnityEngine;

/// <summary>
/// (A-4) 기억 zip의 저장 위치를 결정한다.
///
/// 기존에는 파일명만 넘겨서 RAG가 StreamingAssets 아래에 zip을 썼다.
/// 에디터에서는 프로젝트 폴더라 잘 동작하지만, 빌드 후 Program Files처럼 쓰기 권한이 없는
/// 위치에 설치되면 저장이 조용히 실패한다(에디터에서는 재현되지 않는 결함).
///
/// LLMUnitySetup.GetAssetPath는 내부적으로 Path.Combine(streamingAssetsPath, rel)을 쓰는데,
/// .NET의 Path.Combine은 두 번째 인자가 절대경로면 그것을 그대로 돌려준다.
/// 따라서 절대경로를 넘기기만 하면 LLMUnity를 고치지 않고도 저장 위치를 바꿀 수 있다.
///
/// 추가로, StreamingAssets에 같은 이름의 zip이 들어있으면(배포에 동봉한 시드 기억)
/// 최초 1회 persistentDataPath로 복사한다. 원본은 읽기 전용으로 두고 쓰기는 사본에만 한다.
/// </summary>
public static class MemoryPaths
{
    private const string SubFolder = "Memory";

    /// <summary>기억 파일들이 실제로 저장되는 폴더(쓰기 가능 보장).</summary>
    public static string Root => Path.Combine(Application.persistentDataPath, SubFolder);

    /// <summary>
    /// 파일명을 쓰기 가능한 절대경로로 바꾼다. 필요하면 StreamingAssets의 시드 파일을 복사해 온다.
    /// RAG.Load / RAG.Save 양쪽에 이 결과를 그대로 넘기면 된다.
    /// </summary>
    public static string Resolve(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return fileName;

        // 인스펙터에 이미 절대경로를 넣어둔 경우는 존중한다.
        if (Path.IsPathRooted(fileName)) return fileName;

        string target = Path.Combine(Root, fileName);

        try
        {
            Directory.CreateDirectory(Root);
            if (!File.Exists(target))
                TrySeedFromStreamingAssets(fileName, target);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[MemoryPaths] 저장 폴더 준비 실패 → 기본 경로 사용: {e.Message}");
            return fileName;
        }

        return target;
    }

    /// <summary>배포에 동봉한 시드 기억이 있으면 최초 실행 시 1회 복사.</summary>
    private static void TrySeedFromStreamingAssets(string fileName, string target)
    {
        string seed = Path.Combine(Application.streamingAssetsPath, fileName);

        // Android는 StreamingAssets가 apk 안에 있어 File API로 못 읽는다. 이 프로젝트는 PC 빌드지만 방어해 둔다.
        if (Application.platform == RuntimePlatform.Android) return;
        if (!File.Exists(seed)) return;

        File.Copy(seed, target);
        Debug.Log($"[MemoryPaths] 시드 기억 복사: {fileName} → {target}");
    }
}
