﻿using System;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using System.Collections.Generic;
using System.Linq;

namespace GxtCadSolutions
{
	public class Commands
	{
		Document document = Application.DocumentManager.MdiActiveDocument;
		Database database = Application.DocumentManager.MdiActiveDocument.Database;

		[CommandMethod("EPD")]
		public void ProfileCreator()
		{
			//prompt the user for running line
			PromptEntityOptions promptEntity = new PromptEntityOptions("\nPlease Select Running Line: ");
			promptEntity.SetRejectMessage("Line selected is not a polyline!!");
			promptEntity.AddAllowedClass(typeof(Polyline), true);

			PromptEntityResult entityResult = document.Editor.GetEntity(promptEntity);

			if (entityResult.Status != PromptStatus.OK)
			{
				document.Editor.WriteMessage("Error: Please select a Polyline.");
				return;
			}

			Transaction tr = database.TransactionManager.StartTransaction();
			//Save the polyline
			Polyline runningLine = null;
			using (tr)
			{
				runningLine = tr.GetObject(entityResult.ObjectId, OpenMode.ForRead) as Polyline;
			}

			//create the grid for the profile
			Grid grid = null;
			try
			{
				grid = new Grid(4, runningLine.Length);
				grid.Draw();
			}
			catch (Autodesk.AutoCAD.Runtime.Exception ex)
			{
				document.Editor.WriteMessage("error creating grid;" + ex.Message);
			}

			if (grid != null)
			{
				grid.SaveGrid();
			}

			
			Point3d gradeLineInsPt = grid.InsertionPoint;
			//create a vector to generate the starting point for the gradeline..It 80' from -25 to 0
			Point3d origin = new Point3d(0, 0, 0);
			Matrix3d matrix = Matrix3d.Displacement(origin.GetVectorTo(new Point3d(0, 100, 0)));
			gradeLineInsPt = gradeLineInsPt.TransformBy(matrix);

			Polyline gradeLine = new Polyline();
			Transaction trans = database.TransactionManager.StartTransaction();
			using (trans)
			{
				BlockTable bt = trans.GetObject(database.BlockTableId, OpenMode.ForRead) as BlockTable;
				BlockTableRecord btr = trans.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite) as BlockTableRecord;

				
				gradeLine.AddVertexAt(0, new Point2d(gradeLineInsPt.X, gradeLineInsPt.Y), 0, 0, 0);
				gradeLine.AddVertexAt(1, new Point2d((gradeLineInsPt.X + runningLine.Length), gradeLineInsPt.Y), 0, 0, 0);
				gradeLine.Layer = "PROFILE";

				btr.AppendEntity(gradeLine);
				trans.AddNewlyCreatedDBObject(gradeLine, true);
				trans.Commit();
			}

			//vertice to create selection fence use runningLine
			SelectionSet selectionSet = CreateFenceUsingPolyline(runningLine, false);

			//if any objects selected 
			if (selectionSet != null)
			{
				//find objects that intersect with runningLine
				var objCrossingRunningLine = CrossingRunnngLine(selectionSet, runningLine, gradeLineInsPt);

				Point3dCollection bocPoints = new Point3dCollection();

				//draw profile objects if any
				if (objCrossingRunningLine != null)
				{
					foreach (var obj in objCrossingRunningLine)
					{
						//draw profiles objects of Type = boc 
						if (obj.Type == "boc")
						{
							bocPoints.Add(obj.Center);
						}
						else
						{
							DrawProfileObjects(obj);
						}
					}
				}

				if (bocPoints.Count != 0)
				{
					//convert to array to sort
					Point3d[] point3s = new Point3d[bocPoints.Count];
					bocPoints.CopyTo(point3s, 0);
					Array.Sort(point3s, new sort3dByX());

					Point3dCollection bocPtsSorted = new Point3dCollection(point3s);

					DrawProfileDriveway(bocPtsSorted, gradeLine.Length);
				}
					


				//find boc intersect
				//var bocCrossingRunningLine = 
			}

			//bore line
			Polyline bore = gradeLine.GetOffsetCurves(14)[0] as Polyline;
			DrawBoreLine(bore);

			//find intercepting profile objects using fence and filter for only ellipses
			SelectionSet utilitiesCrossingBore = CreateFenceUsingPolyline(bore, true);

			//if any profile ellipses selected
			if (utilitiesCrossingBore != null)
			{
				//DrawBoreBelowUtilities(utilitiesCrossingBore, bore.ObjectId);
			}
		}

		//private void DrawBoreBelowUtilities(Point3dCollection pts, Polyline bore)
		//{
		//	double verticeIndex;

		//	Transaction trans = database.TransactionManager.StartTransaction();
		//	using (trans)
		//	{
		//		foreach (Point3d pt in pts)
		//		{
		//			Point3d pt1 = new Point3d(pt.X - 40, pt.Y, pt.Z);
		//			Point3d pt2 = new Point3d(pt.X - 20, pt.Y - 8, pt.Z);
		//			Point3d pt3 = new Point3d(pt.X + 20, pt.Y - 8, pt.Z);
		//			Point3d pt4 = new Point3d(pt.X + 40, pt.Y, pt.Z);

		//			try
		//			{
		//				verticeIndex = bore.GetParameterAtPoint(pt1);
		//				var planeXY = new Plane(Point3d.Origin, Vector3d.ZAxis);
		//				//bore.AddVertexAt(verticeIndex, pt1.Convert2d(planeXY), 0, 0, 0);
		//				//bore.AddVertexAt(verticeIndex + 1, pt2.Convert2d(planeXY), 0, 0, 0);
		//				//bore.AddVertexAt(verticeIndex + 2, pt3.Convert2d(planeXY), 0, 0, 0);
		//				//bore.AddVertexAt(verticeIndex + 3, pt4.Convert2d(planeXY), 0, 0, 0);
		//			}
		//			catch (Autodesk.AutoCAD.Runtime.Exception ex)
		//			{
		//				document.Editor.WriteMessage(ex.Message);
		//			}
		//		}
		//		trans.Commit();
		//	}
		//}

		public List<ProfileObject> CrossingRunnngLine(SelectionSet promptSelectionResult, Polyline rl, Point3d glInsPt)
		{
			var profileObjects = new List<ProfileObject>();
			Transaction trans = database.TransactionManager.StartTransaction();

			using (trans)
			{
				//iterate troughth selection set and get intersection points
				foreach (SelectedObject selectedObject in promptSelectionResult)
				{
					Entity ent = (Entity)trans.GetObject(selectedObject.ObjectId, OpenMode.ForRead);

					if (ent.ObjectId == rl.ObjectId)
						continue;

					//try and if they intersect the results will be on points variable
					try
					{
						Point3dCollection points = new Point3dCollection();

						rl.IntersectWith(ent, Intersect.OnBothOperands, points, IntPtr.Zero, IntPtr.Zero);

						//if we are here then all good no error!
						if (points != null && points.Count != 0)
						{
							

							if (ent.Layer.ToLower() == "boc")
							{
								int index = 0;
								while (index < points.Count)
								{
									Point3d ipoint = GetProfileObjInsertionPoint(rl.GetDistAtPoint(points[index]), ent.Layer, glInsPt);
									profileObjects.Add(
									new ProfileObject
									{
										Center = ipoint,
										Type = ent.Layer.ToLower()
									});
									index++;
								}
							}
							else
							{
								double size = GetLineTypeSize(ent.Linetype);
								string contents = ProfileObjContentFormat(ent.Linetype);

								Point3d ipoint = GetProfileObjInsertionPoint(rl.GetDistAtPoint(points[0]), ent.Layer, glInsPt);

								if (size != 0.0)
								{
									profileObjects.Add(
									new ProfileObject
									{
										Center = ipoint,
										Type = ent.Layer,
										Size = size,
										Contents = contents
									});
								}
							}
						}
					}
					catch (Autodesk.AutoCAD.Runtime.Exception e)
					{
						document.Editor.WriteMessage(e.Message + "\n" + ent.BlockName + "Does not Intersect running line.");
					}
				}
			}

			return profileObjects;
		}

		public int GetProfileDepth(string layer)
		{
			int depth;

			switch (layer.ToLower())
			{
				case "sewer":
					depth = 40;
					break;
				case "water":
					depth = 32;
					break;
				case "storm":
					depth = 16;
					break;
				default:
					depth = 0;
					break;
			}
			return depth;
		}

		public Point3d GetProfileObjInsertionPoint(double dist, string layer, Point3d pt)
		{
			Matrix3d matrix;

			Point3d p = new Point3d(0, 0, 0);

			if (layer.ToLower() == "boc")
			{
				matrix = Matrix3d.Displacement(p.GetVectorTo(new Point3d(p.X + dist, p.Y, 0)));
			}
			else
			{
				matrix = Matrix3d.Displacement(p.GetVectorTo(new Point3d(p.X + dist, p.Y - GetProfileDepth(layer), 0)));
			}
				
			pt = pt.TransformBy(matrix);

			return pt;
		}
		public void DrawProfileObjects(ProfileObject obj)
		{
			Transaction trans = database.TransactionManager.StartTransaction();
			using (trans)
			{
				BlockTable bt = trans.GetObject(database.BlockTableId, OpenMode.ForRead) as BlockTable;
				BlockTableRecord btr = trans.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite) as BlockTableRecord;

				Vector3d normal = Vector3d.ZAxis;
				Vector3d majorAxis = (obj.Size * 2) * Vector3d.YAxis;  //2 need to be change to a vertical scale variable / by 2!!!!
				double radiusRatio = .25;
				double startingAngle = 0.0;
				double endAng = 0.0;

				Ellipse ellipse = new Ellipse(
					obj.Center,
					normal,
					majorAxis,
					radiusRatio,
					startingAngle,
					endAng
				);

				DBText dBText = new DBText();
				dBText.Layer = "TEXT-2";
				dBText.Height = 2.2;
				dBText.TextString = obj.Contents;

				Matrix3d matrix = Matrix3d.Displacement(Point3d.Origin.GetVectorTo(new Point3d(obj.Size * 1.25, 0, 0)));
				dBText.Position = obj.Center.TransformBy(matrix);

				btr.AppendEntity(dBText);
				btr.AppendEntity(ellipse);
				trans.AddNewlyCreatedDBObject(dBText, true);
				trans.AddNewlyCreatedDBObject(ellipse, true);
				trans.Commit();
			}
		}

		public void DrawBoreLine(Polyline bore)
		{
			Transaction trans = database.TransactionManager.StartTransaction();
			using (trans)
			{
				BlockTable bt = trans.GetObject(database.BlockTableId, OpenMode.ForRead) as BlockTable;
				BlockTableRecord btr = trans.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite) as BlockTableRecord;

				//append bore down segment
				bore.AddVertexAt(0, new Point2d(bore.StartPoint.X, bore.StartPoint.Y + 14), 0, 0, 0);
				//append bore up segment
				bore.AddVertexAt(bore.NumberOfVertices, new Point2d(bore.EndPoint.X, bore.EndPoint.Y + 14), 0, 0, 0);
				//fillet bore
				Fillet(bore, 3, 0, 1);
				Fillet(bore, 3, bore.NumberOfVertices - 1, bore.NumberOfVertices);

				btr.AppendEntity(bore);
				trans.AddNewlyCreatedDBObject(bore, true);
				trans.Commit();
			}
		}

		public void DrawBoreBelowUtilities(SelectionSet ss, ObjectId boreId)
		{
			Point3dCollection pts = new Point3dCollection();
			//find what intercepts with 
			Transaction trans = database.TransactionManager.StartTransaction();
			using (trans)
			{
				BlockTable bt = trans.GetObject(database.BlockTableId, OpenMode.ForRead) as BlockTable;
				BlockTableRecord btr = trans.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite) as BlockTableRecord;
				//
				Polyline bore = trans.GetObject(boreId, OpenMode.ForWrite) as Polyline;

				//iterate troughth selection set and get intersection points
				foreach (SelectedObject sObj in ss)
				{

					Entity ent = (Entity)trans.GetObject(sObj.ObjectId, OpenMode.ForRead);

					if (ent.ObjectId == bore.ObjectId)
						continue;

					string test = ent.GeometricExtents.MinPoint.ToString();
					test = test.Replace("(", "");
					test = test.Replace(")", "");

					test = "-insert \r boredip \r " + test + " 1 1 0 ";

					document.SendStringToExecute(test, false, false, false);

					////try and if they intersect the results will be on points variable
					//try
					//{
					//	Point3dCollection points = new Point3dCollection();
					//	bore.IntersectWith(ent, Intersect.OnBothOperands, points, IntPtr.Zero, IntPtr.Zero);

					//	//if we are here then all points intersect!!
					//	int ptIntersectCount = points.Count;

					//	for (int i = ptIntersectCount - 1 ; i >= 0; i--)
					//	{
					//		////now that ellipse intercept. Step back 30 feet and add a vertix to the bore
					//		//Point3d pt1 = new Point3d(points[ptIntersectCount].X + 30, points[ptIntersectCount].Y, points[ptIntersectCount].Z);
					//		//int verticeIndex = (int)(bore.GetParameterAtPoint(pt1));
					//		//bore.AddVertexAt(verticeIndex + 1, new Point2d(pt1.X, pt1.Y), 0, 0, 0);

					//	}
					//}
					//catch (Autodesk.AutoCAD.Runtime.Exception e)
					//{
					//	document.Editor.WriteMessage(e.Message + "\n" + ent.BlockName + "Does not Intersect running line.");
					//}

					
					//pts.Add(ent.GeometricExtents.MinPoint);
				}
				trans.Commit();
			}
		}

		public void DrawProfileDriveway(Point3dCollection pts, double gradeLength)
		{
			bool hasBoc = false;
			bool isEndGrade = false;
			DBObjectCollection dbDrivewayObjs = new DBObjectCollection();

			for (int i = 0; i < pts.Count; i++)
			{
				if ((i + 3) > pts.Count)
				{
					//this is the end of the profile 
					isEndGrade = true;
				}	

				//if their distance is less than 1 then they are part of the same driveway or road
				if (pts[i].DistanceTo(pts[i + 1]) < 1 && !isEndGrade)
				{
					if (pts[i + 2].DistanceTo(pts[i + 3]) < 1)
					{
						hasBoc = true;
					}
				}
					

				if (hasBoc)
				{ 
					Line l = new Line(pts[i], new Point3d(pts[i].X, pts[i].Y - 4, pts[i].Z));
					l.Layer = "profile";
					dbDrivewayObjs.Add(l);

					Line l2 = new Line(new Point3d(pts[i].X, pts[i].Y - 4, pts[i].Z), new Point3d(pts[i + 3].X, pts[i + 3].Y - 4, pts[i + 3].Z));
					l2.Layer = "profile";
					dbDrivewayObjs.Add(l2);

					Line l3 = new Line(pts[i + 3], new Point3d(pts[i + 3].X, pts[i + 3].Y - 4, pts[i + 3].Z));
					l3.Layer = "profile";
					dbDrivewayObjs.Add(l3);

					Line l4 = new Line(pts[i + 1], new Point3d(pts[i + 1].X, pts[i + 1].Y - 2, pts[i + 1].Z));
					l4.Layer = "profile";
					dbDrivewayObjs.Add(l4);

					Line l5 = new Line(new Point3d(pts[i + 1].X, pts[i + 1].Y - 2, pts[i + 1].Z), new Point3d(pts[i + 2].X, pts[i + 2].Y - 2, pts[i + 2].Z));
					l5.Layer = "profile";
					dbDrivewayObjs.Add(l5);

					Line l6 = new Line(pts[i + 2], new Point3d(pts[i + 2].X, pts[i + 2].Y - 2, pts[i + 2].Z));
					l6.Layer = "profile";
					dbDrivewayObjs.Add(l6);

					i += 2;
					hasBoc = false;
				}

				if (isEndGrade)
				{
					Line l = new Line(pts[i], new Point3d(pts[i].X, pts[i].Y - 4, pts[i].Z));
					l.Layer = "profile";
					dbDrivewayObjs.Add(l);

					Line l2 = new Line(new Point3d(pts[i].X, pts[i].Y - 4, pts[i].Z), new Point3d(pts[i].X + gradeLength, pts[i].Y - 4, pts[i].Z));
					l2.Layer = "profile";
					dbDrivewayObjs.Add(l2);

					Line l3 = new Line(new Point3d(pts[i].X, pts[i].Y - 2, pts[i].Z), new Point3d(pts[i].X + gradeLength, pts[i].Y - 2, pts[i].Z));
					l3.Layer = "profile";
					dbDrivewayObjs.Add(l3);
					//exit the loop and commit all driveways
					break;
				}
			}

			if (dbDrivewayObjs.Count > 0)
			{
				Transaction trans = database.TransactionManager.StartTransaction();
				using (trans)
				{
					BlockTable bt = trans.GetObject(database.BlockTableId, OpenMode.ForRead) as BlockTable;
					BlockTableRecord btr = trans.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite) as BlockTableRecord;

					foreach (Entity db in dbDrivewayObjs)
					{
						btr.AppendEntity(db);
						trans.AddNewlyCreatedDBObject(db, true);
					}
					trans.Commit();
				}
			}

		}

		public string ProfileObjContentFormat(string size)
		{
			string temp = new String(size.Where(Char.IsDigit).ToArray());
			int.TryParse(temp, out int i);
			temp = i.ToString();
			return temp + "\"" + new String(size.Where(Char.IsLetter).ToArray());
		}

		public double GetLineTypeSize(string size)
		{
			size = new String(size.Where(Char.IsDigit).ToArray());
		
			double.TryParse(size, out double d);
			d /= 12;
			return d;
		}


		private int Fillet(Polyline pline, double radius, int index1, int index2)
		{
			if (pline.GetSegmentType(index1) != SegmentType.Line ||
				pline.GetSegmentType(index2) != SegmentType.Line)
				return 0;
			LineSegment2d seg1 = pline.GetLineSegment2dAt(index1);
			LineSegment2d seg2 = pline.GetLineSegment2dAt(index2);
			Vector2d vec1 = seg1.StartPoint - seg1.EndPoint;
			Vector2d vec2 = seg2.EndPoint - seg2.StartPoint;
			double angle = vec1.GetAngleTo(vec2) / 2.0;
			double dist = radius / Math.Tan(angle);
			if (dist > seg1.Length || dist > seg2.Length)
				return 0;
			Point2d pt1 = seg1.EndPoint.TransformBy(Matrix2d.Displacement(vec1.GetNormal() * dist));
			Point2d pt2 = seg2.StartPoint.TransformBy(Matrix2d.Displacement(vec2.GetNormal() * dist));
			double bulge = Math.Tan((Math.PI / 2.0 - angle) / 2.0);
			if (Clockwise(seg1.StartPoint, seg1.EndPoint, seg2.EndPoint))
				bulge = -bulge;
			pline.AddVertexAt(index2, pt1, bulge, 0.0, 0.0);
			pline.SetPointAt(index2 + 1, pt2);
			return 1;
		}

		private bool Clockwise(Point2d p1, Point2d p2, Point2d p3)
		{
			return ((p2.X - p1.X) * (p3.Y - p1.Y) - (p2.Y - p1.Y) * (p3.X - p1.X)) < 1e-8;
		}

		public SelectionSet CreateFenceUsingPolyline(Polyline pl, bool filter)
		{
			Point3dCollection vertices = new Point3dCollection();
						
			for (double i = 0; i < pl.Length; i += 3)
			{
				vertices.Add(pl.GetPointAtDist(i));
			}
			
			//check for filter
			SelectionFilter selectionFilter = null;
			if (filter)
			{
				TypedValue[] tv = new TypedValue[1];
				tv.SetValue(new TypedValue((int)DxfCode.Start,"ELLIPSE"), 0);
				selectionFilter = new SelectionFilter(tv);
			}

			PromptSelectionResult promptSelectionResult;
			//get fence selection results
			if (selectionFilter != null)
			{
				promptSelectionResult = document.Editor.SelectFence(vertices, selectionFilter);
			}
			else
			{
				promptSelectionResult = document.Editor.SelectFence(vertices);
			}
				
			if (promptSelectionResult.Status != PromptStatus.OK)
			{
				return null;
			}

			return promptSelectionResult.Value;
		}

		//private int GetSegOnPolyline(Polyline pl, Point3d pt)
		//{
		//	bool isOn = false;
		//	int segNum = -1;
		//	for (int i = 0; i < pl.NumberOfVertices; i++)
		//	{
		//		Curve3d seg = null;

		//		SegmentType segType = pl.GetSegmentType(i);

		//		if (segType == SegmentType.Arc)
		//			seg = pl.GetArcSegmentAt(i);
		//		else if (segType == SegmentType.Line)
		//			seg = pl.GetLineSegmentAt(i);

		//		if (seg != null)
		//		{
		//			isOn = seg.IsOn(pt);

		//			if (isOn)
		//			{
		//				segNum = i;
		//				break;
		//			}
		//		}
		//	}
		//	return segNum;
		//}

		[CommandMethod("TEST")]
		public void test()
		{
			Document acDoc = Application.DocumentManager.MdiActiveDocument;

			// Draws a circle and zooms to the extents or 
			// limits of the drawing
			string str = "-insert \r test \r 0,0,0 1 1 0 ";

			acDoc.SendStringToExecute( str, true, false, false);
			
		}


		public class ProfileObject
		{
			public Point3d Center { get; set; }
			public string Type { get; set; }
			public double Size { get; set; }
			public string Contents { get; set; }
		}
	}

	internal class sort3dByX : IComparer<Point3d>

	{

		public static bool IsZero(double a)

		{

			return Math.Abs(a) < Tolerance.Global.EqualPoint;

		}



		public static bool IsEqual(double a, double b)

		{

			return IsZero(b - a);

		}



		public int Compare(Point3d a, Point3d b)

		{

			if (IsEqual(a.X, b.X)) return 0; // ==

			if (a.X < b.X) return -1; // <

			return 1; // >

		}

	}
}
