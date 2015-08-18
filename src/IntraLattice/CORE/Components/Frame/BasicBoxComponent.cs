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



// Summary:     This component generates a simple cartesian 3D lattice.
// ===============================================================================
// Details:     - 
// ===============================================================================
// Author(s):   Aidan Kurtz (http://aidankurtz.com)

namespace IntraLattice.CORE.Components
{
    public class BasicBox : GH_Component
    {

        /// <summary>
        /// Each implementation of GH_Component must provide a public 
        /// constructor without any arguments.
        /// Category represents the Tab in which the component will appear, 
        /// Subcategory the panel. If you use non-existing tab or panel names, 
        /// new tabs/panels will automatically be created.
        /// </summary>
        public BasicBox()
            : base("Basic Box", "BasicBox",
                "Generates a lattice box.",
                "IntraLattice2", "Frame")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddLineParameter("Topology", "Topo", "Unit cell topology", GH_ParamAccess.list);
            pManager.AddNumberParameter("Cell Size ( x )", "CSx", "Size of unit cell (x)", GH_ParamAccess.item, 5); // '5' is the default value
            pManager.AddNumberParameter("Cell Size ( y )", "CSy", "Size of unit cell (y)", GH_ParamAccess.item, 5);
            pManager.AddNumberParameter("Cell Size ( z )", "CSz", "Size of unit cell (z)", GH_ParamAccess.item, 5);
            pManager.AddIntegerParameter("Number of Cells ( x )", "Nx", "Number of unit cells (x)", GH_ParamAccess.item, 5);
            pManager.AddIntegerParameter("Number of Cells ( y )", "Ny", "Number of unit cells (y)", GH_ParamAccess.item, 5);
            pManager.AddIntegerParameter("Number of Cells ( z )", "Nz", "Number of unit cells (z)", GH_ParamAccess.item, 5);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("Struts", "Struts", "Strut curve network", GH_ParamAccess.list);
            pManager.AddPointParameter("Nodes", "Nodes", "Lattice Nodes", GH_ParamAccess.list);
            pManager.HideParameter(1);   // Do not display the 'Nodes' output points
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object can be used to retrieve data from input parameters and 
        /// to store data in output parameters.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // 1. Declare placeholder variables and assign initial invalid data.
            //    This way, if the input parameters fail to supply valid data, we know when to abort
            var topology = new List<Line>();
            double xCellSize = 0;
            double yCellSize = 0;
            double zCellSize = 0;
            int nX = 0;
            int nY = 0;
            int nZ = 0;

            // 2. Retrieve input data.
            if (!DA.GetDataList(0, topology)) { return; }
            if (!DA.GetData(1, ref xCellSize)) { return; }
            if (!DA.GetData(2, ref yCellSize)) { return; }
            if (!DA.GetData(3, ref zCellSize)) { return; }
            if (!DA.GetData(4, ref nX)) { return; }
            if (!DA.GetData(5, ref nY)) { return; }
            if (!DA.GetData(6, ref nZ)) { return; }

            // 3. If data is invalid, we need to abort.
            if (topology.Count < 2) { return; }
            if (xCellSize == 0) { return; }
            if (yCellSize == 0) { return; }
            if (zCellSize == 0) { return; }
            if (nX == 0) { return; }
            if (nY == 0) { return; }
            if (nZ == 0) { return; }

            // 4. Declare our point grid datatree
            var lattice = new Lattice(LatticeType.Uniform);

            // 5. Prepare normalized/formatted unit cell topology
            var cell = new LatticeCell(topology);
            cell.FormatTopology();          // sets up paths for inter-cell nodes
            
            // 6. Define BasePlane and directional iteration vectors
            Plane basePlane = Plane.WorldXY;
            Vector3d vectorX = xCellSize * basePlane.XAxis;
            Vector3d vectorY = yCellSize * basePlane.YAxis;
            Vector3d vectorZ = zCellSize * basePlane.ZAxis;

            float[] N = new float[3] { nX, nY, nZ };

            // 7. Map nodes to design space
            //    Loop through the uvw cell grid
            for (int u = 0; u <= N[0]; u++)
            {
                for (int v = 0; v <= N[1]; v++)
                {
                    for (int w = 0; w <= N[2]; w++)
                    {
                        GH_Path treePath = new GH_Path(u, v, w);                // construct path in tree
                        var nodeList = lattice.Nodes.EnsurePath(treePath);

                        // this loop maps each node in the cell
                        for (int i = 0; i < cell.Nodes.Count; i++)
                        {
                            // if the node belongs to another cell (i.e. it's relative path points outside the current cell)
                            if (cell.NodePaths[i][0] + cell.NodePaths[i][1] + cell.NodePaths[i][2] > 0)
                                continue;
                            
                            double usub = cell.Nodes[i].X; // u-position within unit cell
                            double vsub = cell.Nodes[i].Y; // v-position within unit cell
                            double wsub = cell.Nodes[i].Z; // w-position within unit cell

                            // these conditionals enforce the boundary, no nodes are created beyond the upper boundary
                            if (u == N[0] && usub != 0) continue;
                            if (v == N[1] && vsub != 0) continue;
                            if (w == N[2] && wsub != 0) continue;

                            // compute position vector
                            Vector3d V = (u+usub) * vectorX + (v+vsub) * vectorY + (w+wsub) * vectorZ;

                            var newNode = new LatticeNode(basePlane.Origin + V);    // construct new node with pt
                            
                            nodeList.Add(newNode);                   // add new node to tree
                        }
                    }
                }
            }

            // 7. Generate the struts
            //     Simply loop through all unit cells, and enforce the cell topology (using cellStruts: pairs of node indices)
            var struts = lattice.ConformMapping(cell, N);

            // 8. Set output
            DA.SetDataList(0, struts);            
        }
        
        /// <summary>
        /// Here we set the exposure of the component (i.e. the toolbar panel it is in)
        /// </summary>
        public override GH_Exposure Exposure
        {
            get
            {
                return GH_Exposure.primary;
            }
        }

        /// <summary>
        /// Provides an Icon for every component that will be visible in the User Interface.
        /// Icons need to be 24x24 pixels.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                // You can add image files to your project resources and access them like this:
                //return Resources.circle1;
                return null;
            }
        }

        /// <summary>
        /// Each component must have a unique Guid to identify it. 
        /// It is vital this Guid doesn't change otherwise old ghx files 
        /// that use the old ID will partially fail during loading.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("{3d9572a6-0783-4885-9b11-df464cf549a7}"); }
        }
    }
}