#region Header
// WebcamEventHandler.cs - a Revit external event wraping a webcam image update
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
using System.Threading;
#endregion

namespace RevitWebcam
{
  class WebcamEventHandler : IExternalEventHandler
  {
    #region Member data
    /// <summary>
    /// JPEG image data source URL
    /// </summary>
    //const string _url = "http://vso.aa0.netvolante.jp/record/current.jpg?rand=232200";
    const string _url = "http://www.ggb.ch/webcam.php?e_1__getimage=1"; // matterhorn from zermatt

    /// <summary>
    /// Requested image width.
    /// </summary>
    const int _width = 200;

    /// <summary>
    /// Requested image height.
    /// </summary>
    const int _height = 200;

    /// <summary>
    /// Only update image after this 
    /// time interval has elapsed.
    /// </summary>
    static int _intervalMs = 200;

    /// <summary>
    /// Store the bitmap data in our own structure.
    /// </summary>
    static GreyscaleBitmapData _data;

    /// <summary>
    /// Hash value of last image retrieved.
    /// </summary>
    static byte[] _lastHash = null;

    /// <summary>
    /// Reference to user selected face 
    /// and element to display image onto.
    /// </summary>
    static Reference _faceReference = null;

    /// <summary>
    /// Spatial field primitive index.
    /// </summary>
    static int _sfp_index = -1;

    static bool _running = false;

    /// <summary>
    /// The external event driving this handler
    /// </summary>
    static ExternalEvent _event;
    #endregion // Member data

    #region CompareBytes hash value comparison
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
    #endregion // CompareBytes hash value comparison

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

    #region GetFieldPointsAndValues display bitmap in AVF
    /// <summary>
    /// Determine appropriate field points and values 
    /// to display the given greyscale bitmap image 
    /// data on the given face.
    /// </summary>
    static void GetFieldPointsAndValues(
      ref IList<UV> pts,
      ref IList<ValueAtPoint> valuesAtPoints,
      //ref GreyscaleBitmapData data,
      Face face )
    {
      BoundingBoxUV bb = face.GetBoundingBox();

      double umin = bb.Min.U;
      double umax = bb.Max.U;
      double ustep = ( umax - umin ) / _width; // data.Width
      double u = umin;

      double v = bb.Min.V;
      double vmax = bb.Max.V;
      double vstep = ( vmax - v ) / _height; // data.Height

      List<double> values = new List<double>( 1 );

      for( int y = 0; y < _height; ++y, v += vstep ) // data.Height
      {
        Debug.Assert( v < vmax,
          "expected v to remain within bounds" );

        u = umin;

        for( int x = 0; x < _height; ++x, u += ustep ) // data.Width
        {
          Debug.Assert( u < umax,
            "expected u to remain within bounds" );

          double brightness 
            = _data.GetBrightnessAt( x, y );

          UV uv = new UV( u, v );

          pts.Add( uv );

          values.Clear();

          values.Add( brightness );

          valuesAtPoints.Add( 
            new ValueAtPoint( values ) );
        }
      }
    }
    #endregion // GetFieldPointsAndValues display bitmap in AVF

    /// <summary>
    /// Initialise the external webcam event driver.
    /// </summary>
    public static void Start( View view, Reference r )
    {
      _faceReference = r;

      SpatialFieldManager sfm
        = SpatialFieldManager.GetSpatialFieldManager(
          view );

      if( null == sfm )
      {
        sfm = SpatialFieldManager
          .CreateSpatialFieldManager( view, 1 );
      }
      else if( 0 < _sfp_index )
      {
        sfm.RemoveSpatialFieldPrimitive(
          _sfp_index );

        _sfp_index = -1;
      }

      _sfp_index = sfm.AddSpatialFieldPrimitive(
        _faceReference );

      _event = ExternalEvent.Create( 
        new WebcamEventHandler() );

      Thread thread = new Thread( 
        new ThreadStart( Run ) );

      thread.Start();
    }

    /// <summary>
    /// External webcam event driver.
    /// Check regularly whether the webcam image has 
    /// been updated. If so, update the spatial field 
    /// primitive with the new image data.
    /// Currently, we only display a grey scale image.
    /// Colour images could be handled as well by 
    /// defining a custom colour palette.
    /// </summary>
    static void Run()
    {
      _running = true;

      while( _running )
      {
        _data = new GreyscaleBitmapData(
          _width, _height, _url );

        byte[] hash = _data.HashValue;

        if( null == _lastHash
          || 0 != CompareBytes( hash, _lastHash ) )
        {
          _lastHash = hash;
          _event.Raise();
        }
        Thread.Sleep( _intervalMs );
      }
    }

    /// <summary>
    /// External webcam event handler.
    /// </summary>
    public void Execute( UIApplication uiapp )
    {
      UIDocument uidoc = uiapp.ActiveUIDocument;

      if( null == uidoc )
      {
        _running = false;
      }
      else
      {
        Document doc = uidoc.Document;

        Log( "OnIdling image changed, active document "
          + doc.Title );
        using( Transaction tx = new Transaction( doc ) )
        {
          tx.Start( "Revit Webcam Update" );

          View view = doc.ActiveView; // maybe has to be 3D

          SpatialFieldManager sfm
            = SpatialFieldManager.GetSpatialFieldManager(
              view );

          int nPoints = _width * _height; // _data.Width * _data.Height;

          IList<UV> pts = new List<UV>( nPoints );

          IList<ValueAtPoint> valuesAtPoints
            = new List<ValueAtPoint>( nPoints );

          Element eFace = doc.GetElement(
            _faceReference.ElementId ); // 2013

          Face face = eFace.GetGeometryObjectFromReference(
              _faceReference ) as Face; // 2012

          GetFieldPointsAndValues( ref pts,
            ref valuesAtPoints, face );

          FieldDomainPointsByUV fieldPoints
            = new FieldDomainPointsByUV( pts );

          FieldValues fieldValues
            = new FieldValues( valuesAtPoints );

          int result_index;
          IList<int> registeredResults = sfm.GetRegisteredResults();
          if( 0 == registeredResults.Count )
          {
            AnalysisResultSchema resultSchema
              = new AnalysisResultSchema(
                "Schema 1", "Schema 1 Description" );

            result_index = sfm.RegisterResult(
              resultSchema );
          }
          else
          {
            result_index = registeredResults.First();
          }

          sfm.UpdateSpatialFieldPrimitive(
            _sfp_index, fieldPoints, fieldValues,
            result_index );  // 2012

          //doc.Regenerate(); // done by Commit

          tx.Commit();
        }
      }
    }

    public string GetName()
    {
      return "RevitWebcam external event handler";
    }
  }
}
