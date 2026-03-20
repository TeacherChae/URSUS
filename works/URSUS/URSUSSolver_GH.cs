#r "URSUS.dll"
// Grasshopper Script Component — URSUSSolver wrapper
// URSUS.dll 참조 후 URSUSSolver.Run()을 호출해 SolverResult를 GH 와이어로 출력한다.
//
// 입력 파라미터 (GH 컴포넌트에서 +로 추가):
//   vworldKey  string   VWorld API 키
//   seoulKey   string   서울 열린데이터 API 키
//
// 출력 파라미터:
//   legalCodes      List<string>   법정동 코드
//   names           List<string>   법정동 이름
//   geometries      List<Curve>    법정동 경계 PolylineCurve
//   centroids       List<Point3d>  법정동 중심점
//   areas           List<double>   법정동 면적 (m²)
//   avgIncomes      List<double>   법정동 기준 월 평균 소득
//   unionBoundary   Curve          전체 외곽선

#region Usings
using System;
using System.Collections.Generic;
using Rhino.Geometry;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using URSUS;
#endregion

public class Script_Instance : GH_ScriptInstance
{
    private void RunScript(
        string  vworldKey,
        string  seoulKey,
        ref object legalCodes,
        ref object names,
        ref object geometries,
        ref object centroids,
        ref object areas,
        ref object avgIncomes,
        ref object unionBoundary)
    {
        try
        {
            if (string.IsNullOrEmpty(vworldKey) || string.IsNullOrEmpty(seoulKey))
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "API 키가 비어 있습니다."); return; }

            var solver = new URSUSSolver(vworldKey, seoulKey);
            SolverResult result = solver.Run();

            legalCodes    = result.LegalCodes;
            names         = result.Names;
            geometries    = result.Geometries;
            centroids     = result.Centroids;
            areas         = result.Areas;
            avgIncomes    = result.AvgIncomes;
            unionBoundary = result.UnionBoundary;

            Print($"[Solver] 완료: {result.LegalCodes.Count}개 법정동");
        }
        catch (Exception ex)
        {
            Print($"[ERR] {ex.GetType().Name}: {ex.Message}");
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
        }
    }
}
