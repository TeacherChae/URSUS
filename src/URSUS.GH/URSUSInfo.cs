using System;
using System.Drawing;
using Grasshopper.Kernel;

namespace URSUS.GH
{
    public class URSUSInfo : GH_AssemblyInfo
    {
        public override string Name        => "URSUS";
        public override string Description => "Urban Research with Spatial Utility System";
        public override string Version     => "1.0.0";
        public override string AuthorName  => "TeacherChae";
        public override string AuthorContact => "https://github.com/TeacherChae/URSUS";

        public override Guid Id => new Guid("a049aecf-08a1-40ff-9203-e1e9bf4e9f53");

        public override Bitmap Icon => null;
    }
}
