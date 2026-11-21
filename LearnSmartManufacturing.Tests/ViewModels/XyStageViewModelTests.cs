using LearnSmartManufacturing.Models.Motion;
using LearnSmartManufacturing.Services.Motion;
using LearnSmartManufacturing.ViewModels;

namespace LearnSmartManufacturing.Tests.ViewModels
{
    public class XyStageViewModelTests
    {
        [Fact]
        public void AllRecipePresets_LoadValidPointsStrictlyWithinWorkspace()
        {
            using var stage = new SimulatedXyMotionStage(startBackgroundLoop: false);
            using var vm = new XyStageViewModel(stage);

            foreach (var preset in vm.RecipePresets)
            {
                vm.SelectedRecipePreset = preset;
                vm.LoadSelectedPresetCommand.Execute(null);

                Assert.NotEmpty(vm.TeachPoints);
                for (var i = 0; i < vm.TeachPoints.Count; i++)
                {
                    var pt = vm.TeachPoints[i];
                    Assert.InRange(pt.X, stage.XMinMm, stage.XMaxMm);
                    Assert.InRange(pt.Y, stage.YMinMm, stage.YMaxMm);

                    if (i > 0)
                    {
                        var prev = vm.TeachPoints[i - 1];
                        var plan = XyTrajectoryPlanner.Plan(new Point2D(prev.X, prev.Y), new Point2D(pt.X, pt.Y),
                            pt.Mode, 140, 600, 3000, TrajectoryProfileType.SCurve, pt.ArcRadiusMm);
                        foreach (var sample in plan.SamplePolyline(48))
                        {
                            Assert.InRange(sample.X, stage.XMinMm - 0.5, stage.XMaxMm + 0.5);
                            Assert.InRange(sample.Y, stage.YMinMm - 0.5, stage.YMaxMm + 0.5);
                        }
                    }
                }
            }
        }

        [Fact]
        public void SetTargetFromCanvasAndTeachTableOperations_WorkAndReindex()
        {
            using var stage = new SimulatedXyMotionStage(startBackgroundLoop: false);
            using var vm = new XyStageViewModel(stage);

            vm.ClearPointsCommand.Execute(null);
            Assert.Empty(vm.TeachPoints);

            vm.SetTargetFromCanvas(new Point2D(125.5, 80.0));
            Assert.Equal("125.5", vm.TargetXText);
            Assert.Equal("80", vm.TargetYText);
            Assert.NotNull(vm.PreviewPlan);

            vm.TeachCurrentPointCommand.Execute(null);
            vm.AddTargetAsPointCommand.Execute(null);
            Assert.Equal(2, vm.TeachPoints.Count);
            Assert.Equal(1, vm.TeachPoints[0].Index);
            Assert.Equal(2, vm.TeachPoints[1].Index);

            vm.SelectedTeachPoint = vm.TeachPoints[0];
            vm.RemoveSelectedPointCommand.Execute(null);
            Assert.Single(vm.TeachPoints);
            Assert.Equal(1, vm.TeachPoints[0].Index);
        }
    }
}
