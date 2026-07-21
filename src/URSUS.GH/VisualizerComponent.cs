using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Geometry;
using URSUS.Analysis;
using URSUS.Resources;
using URSUS.Visualization;

namespace URSUS.GH
{
    public sealed record VisualizationTaskOutput(
        VisualizerResult? Result,
        IReadOnlyList<string> MissingCodes,
        string Status,
        string? Error);

    public class VisualizerComponent : GH_TaskCapableComponent<VisualizationTaskOutput>
    {
        private readonly VisualizationMeshCache _meshCache = new();

        public VisualizerComponent()
            : base(
                "URSUS Visualizer", "Viz",
                "행정구역 choropleth/extrusion 및 선택적 IDW trend surface",
                "URSUS", "Visualization")
        {
            UseTasks = true;
        }

        public override Guid ComponentGuid
            => new Guid("392dfc85-c773-4489-938a-188fb90acb50");

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter("Boundary", "B",
                "전체 외곽선 (SolverResult.UnionBoundary)", GH_ParamAccess.item);
            pManager.AddPointParameter("Centroids", "C",
                "법정동 중심점", GH_ParamAccess.list);
            pManager.AddNumberParameter("Values", "V",
                "보간할 값 (SolverResult.Values)", GH_ParamAccess.list);
            pManager.AddNumberParameter("Resolution", "R",
                "메시 최대 엣지 길이", GH_ParamAccess.item, 100.0);
            pManager.AddNumberParameter("Power", "P",
                "IDW 지수", GH_ParamAccess.item, 2.5);
            pManager.AddNumberParameter("Height Scale", "HS",
                "Z 높이 배율", GH_ParamAccess.item, 0.5);
            pManager.AddNumberParameter("Height Ratio", "HR",
                "Z 최대 높이 = bboxWidth × HR", GH_ParamAccess.item, 0.5);
            pManager.AddIntegerParameter("Legend Steps", "LS",
                "범례 단계 수", GH_ParamAccess.item, 8);
            pManager.AddIntegerParameter("Color Style", "CS",
                "0=Custom 1=BlueRed 2=Heatmap 3=Spectral 4=Viridis 5=Diverging 6=Grayscale",
                GH_ParamAccess.item, 4);
            pManager.AddColourParameter("Color Low", "CL",
                "colorStyle=0일 때 최솟값 색상",
                GH_ParamAccess.item, Color.FromArgb(44, 123, 182));
            pManager.AddColourParameter("Color High", "CH",
                "colorStyle=0일 때 최댓값 색상",
                GH_ParamAccess.item, Color.FromArgb(215, 25, 28));
            pManager.AddIntegerParameter("Mode", "Mode",
                "0=Choropleth 1=Extrusion 2=IDW", GH_ParamAccess.item, 0);
            pManager.AddGenericParameter("Snapshot", "S",
                "Solver의 immutable AnalysisSnapshot", GH_ParamAccess.item);
            pManager[12].Optional = true;
            pManager.AddTextParameter("Layer Id", "Layer",
                "raw snapshot layer id. 비우면 기존 Values overlay", GH_ParamAccess.item, "");
            pManager[13].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddMeshParameter("Mesh", "M",
                "Z 변위 + 컬러 메시", GH_ParamAccess.item);
            pManager.AddMeshParameter("Flat Mesh", "FM",
                "Z=0 컬러 메시 (지도 오버레이용)", GH_ParamAccess.item);
            pManager.AddNumberParameter("Min Value", "Min",
                "범례 최솟값", GH_ParamAccess.item);
            pManager.AddNumberParameter("Max Value", "Max",
                "범례 최댓값", GH_ParamAccess.item);
            pManager.AddMeshParameter("Legend Mesh", "LM",
                "범례 그라디언트 바", GH_ParamAccess.item);
            pManager.AddGenericParameter("Legend Dots", "LD",
                "범례 값 레이블 (TextDot 리스트)", GH_ParamAccess.list);
            pManager.AddTextParameter("Missing Codes", "Missing",
                "geometry 또는 값이 없는 법정동 코드", GH_ParamAccess.list);
            pManager.AddTextParameter("Status", "ST",
                "mode/layer/cache/unit 상태", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var input = ReadInput(DA);
            if (input == null) return;

            if (InPreSolve)
            {
                CancellationToken generationToken = CancelToken;
                Task<VisualizationTaskOutput> task = Task.Run(
                    () => ComputeSafe(input, generationToken), generationToken);
                TaskList.Add(task.ContinueWith(completed =>
                {
                    if (completed.Status == TaskStatus.RanToCompletion) return completed.Result;
                    input.Dispose();
                    return new VisualizationTaskOutput(null, Array.Empty<string>(),
                        completed.IsCanceled ? "Canceled" : "Faulted",
                        completed.Exception?.GetBaseException().Message);
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default));
                return;
            }

            VisualizationTaskOutput output;
            if (!GetSolveResults(DA, out output))
                output = ComputeSafe(input, CancellationToken.None);
            else
                input.Dispose();
            WriteOutput(DA, output);
        }

        private VisualizationInput? ReadInput(IGH_DataAccess DA)
        {
            int modeValue = 0, legendSteps = 8, colorStyle = 4;
            double resolution = 100, power = 2.5, heightScale = 0.5, heightRatio = 0.5;
            Color colorLow = Color.FromArgb(44, 123, 182);
            Color colorHigh = Color.FromArgb(215, 25, 28);
            object? snapshotValue = null;
            string layerId = "";
            var centroids = new List<Point3d>();
            var values = new List<double>();
            Curve? boundary = null;

            DA.GetData(11, ref modeValue);
            DA.GetData(12, ref snapshotValue);
            DA.GetData(13, ref layerId);
            DA.GetDataList(2, values);
            var mode = Enum.IsDefined(typeof(VisualizationMode), modeValue)
                ? (VisualizationMode)modeValue : VisualizationMode.Choropleth;
            AnalysisSnapshot? snapshot = UnwrapSnapshot(snapshotValue);

            bool needsLegacyGeometry = snapshot == null;
            if (needsLegacyGeometry)
            {
                if (!DA.GetData(0, ref boundary)) return null;
                if (snapshot == null && !DA.GetDataList(1, centroids)) return null;
                if (snapshot == null && values.Count == 0) return null;
            }
            DA.GetData(3, ref resolution);
            DA.GetData(4, ref power);
            DA.GetData(5, ref heightScale);
            DA.GetData(6, ref heightRatio);
            DA.GetData(7, ref legendSteps);
            DA.GetData(8, ref colorStyle);
            DA.GetData(9, ref colorLow);
            DA.GetData(10, ref colorHigh);

            RhinoDoc? document = RhinoDoc.ActiveDoc;
            double lengthScale = document == null ? 1.0
                : RhinoMath.UnitScale(UnitSystem.Meters, document.ModelUnitSystem);
            double metersPerDocumentUnit = double.IsFinite(lengthScale) && lengthScale > 0
                ? 1.0 / lengthScale : 0;
            return new VisualizationInput(boundary?.DuplicateCurve(), centroids.ToArray(), values.ToArray(),
                resolution, power, heightScale, heightRatio, legendSteps, colorStyle,
                colorLow, colorHigh, mode, snapshot, layerId,
                metersPerDocumentUnit, document?.ModelAbsoluteTolerance ?? 0.001);
        }

        private VisualizationTaskOutput ComputeSafe(VisualizationInput input, CancellationToken token)
        {
            try
            {
                token.ThrowIfCancellationRequested();
                if (input.Snapshot != null && input.Mode != VisualizationMode.Idw)
                {
                    var output = SnapshotVisualizationAdapter.Build(input.Snapshot, input.LayerId,
                        input.Values, input.Mode, input.HeightScale, input.LegendSteps,
                        input.ColorStyle, input.ColorLow, input.ColorHigh, _meshCache,
                        input.MetersPerDocumentUnit, input.Tolerance, token);
                    return new VisualizationTaskOutput(output.Result, output.MissingCodes,
                        output.Status, null);
                }

                List<Point3d> points;
                List<double> values;
                IReadOnlyList<IReadOnlyList<Curve>> domains;
                List<IReadOnlyList<Curve>>? ownedDomains = null;
                string? cacheKey = null;
                string? unit = null;
                bool normalized = true;
                IReadOnlyList<string> missing = Array.Empty<string>();
                string status;
                if (input.Snapshot != null)
                {
                    var plan = ChoroplethPlanner.Create(input.Snapshot, input.LayerId,
                        VisualizationMode.Choropleth, 0, input.Values);
                    var unitScale = DocumentUnitScale.FromMetersPerDocumentUnit(
                        input.MetersPerDocumentUnit);
                    double scale = unitScale.LengthScale;
                    var finite = plan.Districts.Where(district => !district.IsMissing).ToArray();
                    points = finite.Select(district => new Point3d(
                        district.Topology.Centroid.X * scale,
                        district.Topology.Centroid.Y * scale, 0)).ToList();
                    values = finite.Select(district => district.RawValue).ToList();
                    cacheKey = plan.SnapshotKey;
                    unit = plan.Unit;
                    normalized = plan.IsNormalized;
                    missing = plan.MissingCodes;
                    ownedDomains = SnapshotVisualizationAdapter.CreateDomainRegions(
                        input.Snapshot, scale, token);
                    domains = ownedDomains;
                    status = $"IDW · {plan.LayerId} · preview · missing {missing.Count} · " +
                        SnapshotVisualizationAdapter.DescribeProvenance(input.Snapshot, plan.LayerId) +
                        (string.IsNullOrWhiteSpace(unitScale.Warning)
                            ? "" : $" · {unitScale.Warning}");
                }
                else
                {
                    if (input.Boundary == null)
                        throw new ArgumentException("IDW boundary가 없습니다.");
                    points = input.Centroids.ToList();
                    values = input.Values.ToList();
                    domains = new IReadOnlyList<Curve>[] { new[] { input.Boundary } };
                    status = "IDW · legacy fallback · preview";
                }
                try
                {
                    var visualizer = new IDWVisualizer(points, values, input.Resolution, input.Power,
                        input.HeightScale, input.HeightRatio, input.LegendSteps, input.ColorStyle,
                        input.ColorLow, input.ColorHigh, normalized, unit, _meshCache);
                    var result = visualizer.Build(domains, token, VisualizationQuality.Preview,
                        cacheKey, DocumentUnitScale.FromMetersPerDocumentUnit(
                            input.MetersPerDocumentUnit).LengthScale);
                    return new VisualizationTaskOutput(result, missing, status, null);
                }
                finally
                {
                    if (ownedDomains != null)
                        foreach (Curve curve in ownedDomains.SelectMany(region => region)) curve.Dispose();
                }
            }
            catch (OperationCanceledException)
            {
                return new VisualizationTaskOutput(null, Array.Empty<string>(), "Canceled", null);
            }
            catch (Exception ex)
            {
                return new VisualizationTaskOutput(null, Array.Empty<string>(), "Faulted", ex.Message);
            }
            finally { input.Dispose(); }
        }

        private void WriteOutput(IGH_DataAccess DA, VisualizationTaskOutput output)
        {
            if (output.Result != null)
            {
                DA.SetData(0, output.Result.Mesh);
                DA.SetData(1, output.Result.FlatMesh);
                DA.SetData(2, output.Result.MinVal);
                DA.SetData(3, output.Result.MaxVal);
                DA.SetData(4, output.Result.LegendMesh);
                DA.SetDataList(5, output.Result.LegendDots);
            }
            DA.SetDataList(6, output.MissingCodes);
            DA.SetData(7, output.Status);
            if (!string.IsNullOrWhiteSpace(output.Error))
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    ErrorGuideMap.FormatMessageWithGuide(ErrorCodes.VisualizationFailed,
                        ErrorMessages.Visualization.VisualizationFailed(output.Error)));
        }

        private static AnalysisSnapshot? UnwrapSnapshot(object? value)
        {
            if (value is AnalysisSnapshot snapshot) return snapshot;
            if (value is IGH_Goo goo && goo.CastTo(out AnalysisSnapshot cast)) return cast;
            return null;
        }

        public override void RemovedFromDocument(GH_Document document)
        {
            base.RequestTaskCancellation();
            _meshCache.Dispose();
            base.RemovedFromDocument(document);
        }

        private sealed record VisualizationInput(
            Curve? Boundary,
            IReadOnlyList<Point3d> Centroids,
            IReadOnlyList<double> Values,
            double Resolution,
            double Power,
            double HeightScale,
            double HeightRatio,
            int LegendSteps,
            int ColorStyle,
            Color ColorLow,
            Color ColorHigh,
            VisualizationMode Mode,
            AnalysisSnapshot? Snapshot,
            string LayerId,
            double MetersPerDocumentUnit,
            double Tolerance) : IDisposable
        {
            public void Dispose() => Boundary?.Dispose();
        }
    }
}
