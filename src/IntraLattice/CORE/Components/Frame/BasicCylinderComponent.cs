﻿using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Collections;
using Rhino;
using IntraLattice.Properties;
using Grasshopper;
using IntraLattice.CORE.Data;
using IntraLattice.CORE.Components;
using IntraLattice.CORE.Helpers;

// Summary:     This component generates a simple cylindrical lattice.
// ===============================================================================
// Details:     - 
// ===============================================================================
// Author(s):   Aidan Kurtz (http://aidankurtz.com)

namespace IntraLattice.CORE.Components
{
    public class BasicCylinder : GH_Component
    {
        public BasicCylinder()
            : base("Basic Cylinder", "BasicCylinder",
                "Generates a conformal lattice cylinder.",
                "IntraLattice2", "Frame")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddLineParameter("Topology", "Topo", "Unit cell topology", GH_ParamAccess.list);
            pManager.AddNumberParameter("Radius", "R", "Radius of cylinder", GH_ParamAccess.item, 15);
            pManager.AddNumberParameter("Height", "H", "Height of cylinder", GH_ParamAccess.item, 25);
            pManager.AddIntegerParameter("Number u", "Nu", "Number of unit cells (axial)", GH_ParamAccess.item, 5);
            pManager.AddIntegerParameter("Number v", "Nv", "Number of unit cells (theta)", GH_ParamAccess.item, 15);
            pManager.AddIntegerParameter("Number w", "Nw", "Number of unit cells (radial)", GH_ParamAccess.item, 4);
            pManager.AddBooleanParameter("Morph", "Morph", "If true, struts are morphed to the space as curves.", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("Struts", "Struts", "Strut curve network", GH_ParamAccess.list);
            pManager.AddPointParameter("Nodes", "Nodes", "Lattice Nodes", GH_ParamAccess.list);
            pManager.HideParameter(1);  // Do not display the 'Nodes' output points
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // 1. Retrieve and validate data
            var topology = new List<Line>();
            double radius = 0;
            double height = 0;
            int nU = 0;
            int nV = 0;
            int nW = 0;
            bool morphed = false;

            if (!DA.GetDataList(0, topology)) { return; }
            if (!DA.GetData(1, ref radius)) { return; }
            if (!DA.GetData(2, ref height)) { return; }
            if (!DA.GetData(3, ref nU)) { return; }
            if (!DA.GetData(4, ref nV)) { return; }
            if (!DA.GetData(5, ref nW)) { return; }
            if (!DA.GetData(6, ref morphed)) { return; }

            if (topology.Count < 2) { return; }
            if (radius == 0) { return; }
            if (height == 0) { return; }
            if (nU == 0) { return; }
            if (nV == 0) { return; }
            if (nW == 0) { return; }

            // 2. Initialize the lattice
            var latticeType = morphed ?  LatticeType.MorphUVW : LatticeType.ConformUVW;
            var lattice = new Lattice(latticeType);
            var spaceTree = new DataTree<GeometryBase>(); // will contain the morphed uv spaces (as surface-surface, surface-axis or surface-point)
            
            // 3. Define cylinder
            Plane basePlane = Plane.WorldXY;
            Surface cylinder = ( new Cylinder(new Circle(basePlane, radius), height) ).ToNurbsSurface();
            cylinder = cylinder.Transpose();
            LineCurve axis = new LineCurve(basePlane.Origin, basePlane.Origin + height*basePlane.ZAxis);

            // 4. Package the number of cells in each direction into an array
            float[] N = new float[3] { nU, nV, nW };

            // 5. Normalize the UV-domain
            Interval unitDomain = new Interval(0, 1);
            cylinder.SetDomain(0, unitDomain); // surface u-direction
            cylinder.SetDomain(1, unitDomain); // surface v-direction
            axis.Domain = unitDomain;

            // 6. Prepare normalized/formatted unit cell topology
            var cell = new LatticeCell(topology);
            cell.FormatTopology();          // sets up paths for inter-cell nodes

            // 7. Create grid of points (as data tree)
            //    u-direction is along the cylinder
            for (int u = 0; u <= N[0]; u++)
            {
                // v-direction travels around the cylinder axis (about axis)
                for (int v = 0; v <= N[1]; v++)
                {
                    
                    // this loop maps each node index in the cell onto the UV-surface maps
                    for (int i = 0; i < cell.Nodes.Count; i++)
                    {
                        // if the node belongs to another cell (i.e. it's relative path points outside the current cell)
                        if (cell.NodePaths[i][0] + cell.NodePaths[i][1] + cell.NodePaths[i][2] > 0)
                            continue;

                        Point3d pt1, pt2;
                        Vector3d[] derivatives;

                        double usub = cell.Nodes[i].X; // u-position within unit cell
                        double vsub = cell.Nodes[i].Y; // v-position within unit cell
                        double wsub = cell.Nodes[i].Z; // w-position within unit cell

                        // construct z-position vector
                        Vector3d vectorZ = height * basePlane.ZAxis * (u+usub) / N[0];
                        pt1 = basePlane.Origin + vectorZ;                                                   // compute pt1 (is on axis)
                        cylinder.Evaluate( (u+usub)/N[0], (v+vsub)/N[1], 2, out pt2, out derivatives);      // compute pt2, and derivates (on surface)

                        // create vector joining these two points
                        Vector3d wVect = pt2 - pt1;

                        // create grid points on and between surface and axis
                        for (int w = 0; w <= N[2]; w++)
                        {
                            // these conditionals enforce the boundary, no nodes are created beyond the upper boundary
                            if (u == N[0] && usub != 0) continue;
                            if (v == N[1] && vsub != 0) continue;
                            if (w == N[2] && wsub != 0) continue;

                            // add point to gridTree
                            var newNode = new LatticeNode(pt1 + wVect * (w+wsub)/N[2]);
                            GH_Path treePath = new GH_Path(u, v, w, i);
                            lattice.Nodes.Add(newNode, treePath);
                        }
                    }

                    // Define the uv space map
                    if (u < N[0] && v < N[1])
                    {
                        GH_Path spacePath = new GH_Path(u, v);
                        var uInterval = new Interval((u) / N[0], (u + 1) / N[0]);                   // set trimming interval
                        var vInterval = new Interval((v) / N[1], (v + 1) / N[1]);
                        Surface ss1 = cylinder.Trim(uInterval, vInterval);                          // create sub-surface
                        Curve ss2 = axis.Trim(uInterval);
                        ss1.SetDomain(0, unitDomain); ss1.SetDomain(1, unitDomain);             // normalize domains
                        ss2.Domain = unitDomain;
                        // Save to the space tree
                        spaceTree.Add(ss1, spacePath);
                        spaceTree.Add(ss2, spacePath);
                    }
                }
            }

            // 7. Generate the struts using a mapping method
            var struts = new List<Curve>();
            if (morphed) struts = lattice.MorphMapping(cell, spaceTree, N);
            else struts = lattice.ConformMapping(cell, N);

            // 8. Set output
            DA.SetDataList(0, struts);            
        }

        // Primitive grid component -> first panel of the toolbar
        public override GH_Exposure Exposure
        {
            get
            {
                return GH_Exposure.primary;
            }
        }

        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                // You can add image files to your project resources and access them like this:
                //return Resources.circle2;
                return null;
            }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("{9f6769c0-dec5-4a0d-8ade-76fca1dfd4e3}"); }
        }
    }
}