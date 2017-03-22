#region Header
//
// RevitWebcam/Command.cs - display a real-time updating webcam image on a selected Revit building element face
//
// The image can be grabbed from the internet or a webcam and is displayed in the Revit model, 
// updating regularly on the Idling event.
//
// Copyright (C) 2010-2012 by Jeremy Tammik, Autodesk Inc. All rights reserved.
//
// Permission to use, copy, modify, and distribute this software
// for any purpose and without fee is hereby granted, provided
// that the above copyright notice appears in all copies and
// that both that copyright notice and the limited warranty and
// restricted rights notice below appear in all supporting
// documentation.
//
// AUTODESK PROVIDES THIS PROGRAM "AS IS" AND WITH ALL FAULTS.
// AUTODESK SPECIFICALLY DISCLAIMS ANY IMPLIED WARRANTY OF
// MERCHANTABILITY OR FITNESS FOR A PARTICULAR USE.  AUTODESK, INC.
// DOES NOT WARRANT THAT THE OPERATION OF THE PROGRAM WILL BE
// UNINTERRUPTED OR ERROR FREE.
//
// Use, duplication, or disclosure by the U.S. Government is subject to
// restrictions set forth in FAR 52.227-19 (Commercial Computer
// Software - Restricted Rights) and DFAR 252.227-7013(c)(1)(ii)
// (Rights in Technical Data and Computer Software), as applicable.
#endregion // Header

#region Namespaces
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Analysis;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using Autodesk.Revit.UI.Selection;
#endregion

namespace RevitWebcam
{
  [Transaction( TransactionMode.Manual )]
  public class Command : IExternalCommand
  {
    #region Member data
    /// <summary>
    /// JPEG image data source URL
    /// </summary>
    const string _url = "http://vso.aa0.netvolante.jp/record/current.jpg?rand=232200"; // picadilly circus
    //const string _url = "http://www.ggb.ch/webcam.php?e_1__getimage=1"; // matterhorn from zermatt

    /// <summary>
    /// Requested image width.
    /// </summary>
    const int _width = 200;

    /// <summary>
    /// Requested image height.
    /// </summary>
    const int _height = 200;

    /// <summary>
    /// Prompt user to select a face to display image onto.
    /// </summary>
    const string _prompt = "Please pick a face to display webcam image";

    /// <summary>
    /// Only update image after a certain time interval has elapsed.
    /// </summary>
    static TimeSpan _interval = new TimeSpan( 0, 0, 0, 0, 5000 );

    /// <summary>
    /// Hash value of last image retrieved.
    /// </summary>
    static byte[] _lastHash = null;

    /// <summary>
    /// Last update of image was performed at.
    /// </summary>
    static DateTime _lastUpdate = DateTime.Now.Subtract( _interval );

    /// <summary>
    /// Reference to user selected face to display image onto.
    /// </summary>
    static Reference _faceReference = null;

    /// <summary>
    /// Spatial field primitive index.
    /// </summary>
    static int _sfp_index = -1;
    #endregion // Member data

    #region Log message
    /// <summary>
    /// Display a message with a time stamp in the Visual Studio debug output window.
    /// </summary>
    static void Log( string msg )
    {
      string dt = DateTime.Now.ToString( "u" );
      Debug.Print( dt + " " + msg );
    }
    #endregion // Log message

    #region CompareBytes for hash value comparison
    /// <summary>
    /// Compare two byte arrays and return 0 if they are equal, 
    /// else a negative or positive number depending on whether 
    /// the first one compares smaller or larger than the second.
    /// </summary>
    static int CompareBytes( byte[] a, byte[] b )
    {
      int n = a.Length;
      int d = n - b.Length;

      if( 0 == d )
      {
        for( int i = 0; i < n && 0 == d; ++i )
        {
          d = a[i] - b[i];
        }
      }
      return d;
    }
    #endregion // CompareBytes for hash value comparison

    #region BimElementFilter to allow selection of building elements only
    class BimElementFilter : ISelectionFilter
    {
      /// <summary>
      /// Allow building elements to be selected.
      /// </summary>
      /// <param name="element">A candidate element in selection operation.</param>
      /// <returns>Return true for elements whose category contributes material to the building model, false for all other elements.</returns>
      public bool AllowElement( Element e )
      {
        return null != e.Category
          && e.Category.HasMaterialQuantities;
      }

      /// <summary>
      /// Allow all the reference to be selected
      /// </summary>
      /// <param name="refer">A candidate reference in selection operation.</param>
      /// <param name="point">The 3D position of the mouse on the candidate reference.</param>
      /// <returns>Return true to allow the user to select this candidate reference.</returns>
      public bool AllowReference( Reference r, XYZ p )
      {
        return true;
      }
    }
    #endregion // BimElementFilter to allow selection of building elements only

    #region SetAnalysisDisplayStyle for active view
    /// <summary>
    /// Set analysis display style to switch off grid lines
    /// and use greyscale values in active view.
    /// </summary>
    void SetAnalysisDisplayStyle( Document doc )
    {
      AnalysisDisplayStyle analysisDisplayStyle;

      const string styleName
        = "Revit Webcam Display Style";

      // extract existing display styles with specific name

      FilteredElementCollector a
        = new FilteredElementCollector( doc );

      IList<Element> elements = a
        .OfClass( typeof( AnalysisDisplayStyle ) )
        .Where( x => x.Name.Equals( styleName ) )
        .Cast<Element>()
        .ToList();

      if( 0 < elements.Count )
      {
        // use the existing display style

        analysisDisplayStyle = elements[0]
          as AnalysisDisplayStyle;
      }
      else
      {
        // create new display style:

        // coloured surface settings:

        AnalysisDisplayColoredSurfaceSettings
          coloredSurfaceSettings
            = new AnalysisDisplayColoredSurfaceSettings();

        coloredSurfaceSettings.ShowGridLines = false;

        // color settings:

        AnalysisDisplayColorSettings colorSettings
          = new AnalysisDisplayColorSettings();

        colorSettings.MaxColor = new Color( 255, 255, 255 );
        colorSettings.MinColor = new Color( 0, 0, 0 );

        // legend settings:

        AnalysisDisplayLegendSettings legendSettings
          = new AnalysisDisplayLegendSettings();

        legendSettings.NumberOfSteps = 10;
        legendSettings.Rounding = 0.05;
        legendSettings.ShowDataDescription = false;
        legendSettings.ShowLegend = true;

        // extract legend text:

        a = new FilteredElementCollector( doc );

        elements = a
          .OfClass( typeof( TextNoteType ) )
          .Where( x => x.Name == "LegendText" )
          .Cast<Element>()
          .ToList();

        if( 0 < elements.Count )
        {
          // if LegendText exists, use it for this display style

          TextNoteType textType = elements[0] as TextNoteType;

          legendSettings.SetTextTypeId( textType.Id, doc );
        }

        // create the analysis display style:
        var transaction = new Transaction( doc );
        transaction.Start( "Create AnalysisDisplayStyle" );
        try
        {
          analysisDisplayStyle = AnalysisDisplayStyle
            .CreateAnalysisDisplayStyle(
              doc, styleName, coloredSurfaceSettings,
              colorSettings, legendSettings );

          // assign the display style to the active view

          doc.ActiveView.AnalysisDisplayStyleId
            = analysisDisplayStyle.Id;

          transaction.Commit();
        }
        catch
        {
          transaction.RollBack();
          throw;
        }
      }
    }
    #endregion // SetAnalysisDisplayStyle for active view

    #region GetFieldPointsAndValues for displaying greyscale bitmap image data in spatial field primitive
    /// <summary>
    /// Determine appropriate field points and values to display 
    /// the given greyscale bitmap image data on the given face.
    /// </summary>
    static void GetFieldPointsAndValues(
      ref IList<UV> pts,
      ref IList<ValueAtPoint> valuesAtPoints,
      ref GreyscaleBitmapData data,
      Face face )
    {
      BoundingBoxUV bb = face.GetBoundingBox();

      double umin = bb.Min.U;
      double umax = bb.Max.U;
      double ustep = ( umax - umin ) / data.Width;
      double u = umin;

      double v = bb.Min.V;
      double vmax = bb.Max.V;
      double vstep = ( vmax - v ) / data.Height;

      List<double> values = new List<double>( 1 );

      for( int y = 0; y < data.Height; ++y, v += vstep )
      {
        Debug.Assert( v < vmax,
          "expected v to remain within bounds" );

        u = umin;

        for( int x = 0; x < data.Width; ++x, u += ustep )
        {
          Debug.Assert( u < umax,
            "expected u to remain within bounds" );

          double brightness = data.GetBrightnessAt(
            x, y );

          UV uv = new UV( u, v );
          pts.Add( uv );

          values.Clear();
          values.Add( brightness );
          valuesAtPoints.Add( new ValueAtPoint(
            values ) );
        }
      }
    }
    #endregion // GetFieldPointsAndValues for displaying greyscale bitmap image data in spatial field primitive

    #region OnIdling Idling event handler
    /// <summary>
    /// Handle Revit Idling event.
    /// If less time elapsed than the specified interval, return immediately.
    /// Otherwise, download the current image frile from the specified URL.
    /// If it has not changed since the last update, return immediately.
    /// Otherwise, update the spatial field primitive with the new image data.
    /// Currently, we only display a grey scale image.
    /// Colour images could be handled as well by defining a custom colour palette.
    /// </summary>
    static void OnIdling(
      object sender,
      IdlingEventArgs e )
    {
      if( DateTime.Now.Subtract( _lastUpdate )
        > _interval )
      {
        Log( "OnIdling" );

        GreyscaleBitmapData data
          = new GreyscaleBitmapData(
            _width, _height, _url );

        byte[] hash = data.HashValue;

        if( null == _lastHash
          || 0 != CompareBytes( hash, _lastHash ) )
        {
          _lastHash = hash;

          // access active document from sender:

          Application app = sender as Application;

          Debug.Assert( null != app,
            "expected a valid Revit application instance" );

          UIApplication uiapp = new UIApplication( app );
          UIDocument uidoc = uiapp.ActiveUIDocument;
          Document doc = uidoc.Document;

          Log( "OnIdling image changed, active document "
            + doc.Title );

          Transaction transaction
            = new Transaction( doc, "Revit Webcam Update" );

          transaction.Start();

          View view = doc.ActiveView; // maybe has to be 3D

          SpatialFieldManager sfm
            = SpatialFieldManager.GetSpatialFieldManager(
              view );

          if( null == sfm )
          {
            sfm = SpatialFieldManager
              .CreateSpatialFieldManager( view, 1 );
          }

          if( 0 > _sfp_index )
          {
            _sfp_index = sfm.AddSpatialFieldPrimitive(
              _faceReference );
          }

          int nPoints = data.Width * data.Height;

          IList<UV> pts = new List<UV>( nPoints );

          IList<ValueAtPoint> valuesAtPoints
            = new List<ValueAtPoint>( nPoints );

          Face face = _faceReference.GeometryObject
            as Face;

          GetFieldPointsAndValues( ref pts,
            ref valuesAtPoints, ref data, face );

          FieldDomainPointsByUV fieldPoints
            = new FieldDomainPointsByUV( pts );

          FieldValues fieldValues
            = new FieldValues( valuesAtPoints );

          sfm.UpdateSpatialFieldPrimitive(
            _sfp_index, fieldPoints, fieldValues );

          doc.Regenerate();
          transaction.Commit();

          _lastUpdate = DateTime.Now;
        }
      }
    }
    #endregion // OnIdling Idling event handler

    #region Execute method for external command add-in mainline
    public Result Execute(
      ExternalCommandData commandData,
      ref string message,
      ElementSet elements )
    {
      try
      {
        UIApplication uiapp = commandData.Application;
        UIDocument uidoc = uiapp.ActiveUIDocument;
        Document doc = uidoc.Document;
        View view = doc.ActiveView; // maybe has to be 3D

        Reference r = uidoc.Selection.PickObject(
          ObjectType.Face,
          new BimElementFilter(),
          _prompt );

        Debug.Assert( null != r,
          "expected non-null reference from PickObject" );

        Debug.Assert( null != r.Element,
          "expected non-null element from PickObject" );

        Debug.Assert( null != r.GeometryObject,
          "expected non-null geometry object from PickObject" );

        _faceReference = r;

        SpatialFieldManager sfm
          = SpatialFieldManager.GetSpatialFieldManager(
            view );

        if( null != sfm && 0 < _sfp_index )
        {
          sfm.RemoveSpatialFieldPrimitive(
            _sfp_index );

          _sfp_index = -1;
        }

        SetAnalysisDisplayStyle( doc );

        uiapp.Idling
          += new EventHandler<IdlingEventArgs>(
            OnIdling );

        return Result.Succeeded;
      }
      catch( Exception ex )
      {
        message = ex.Message;
        return Result.Failed;
      }
    }
    #endregion // Execute method for external command add-in mainline
  }
}

// C:\tmp\wall.rvt
// C:\a\lib\revit\2011\SDK\Samples\AnalysisVisualizationFramework\DistanceToSurfaces\DistanceToSurfaces.rvt
// C:\a\lib\revit\2011\SDK\Samples\AnalysisVisualizationFramework\SpatialFieldGradient\SpatialFieldGradient.rvt
