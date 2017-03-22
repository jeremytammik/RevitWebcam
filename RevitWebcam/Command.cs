#region Header
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
using Autodesk.Revit.UI.Selection;
#endregion

namespace RevitWebcam
{
  [Transaction( TransactionMode.Manual )]
  public class Command : IExternalCommand
  {
    /// <summary>
    /// Prompt user to select face to display image on.
    /// </summary>
    const string _prompt
      = "Please pick a face to display webcam image";

    #region BimElementFilter to select building elements only
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
      /// <param name="r">A candidate reference in selection operation.</param>
      /// <param name="p">The 3D position of the mouse on the candidate reference.</param>
      /// <returns>Return true to allow the user to select this candidate reference.</returns>
      public bool AllowReference( Reference r, XYZ p )
      {
        return true;
      }
    }
    #endregion // BimElementFilter to select building elements only

    #region SetAnalysisDisplayStyle for active view
    /// <summary>
    /// Set analysis display style to switch off grid 
    /// lines and use greyscale values in active view.
    /// </summary>
    void SetAnalysisDisplayStyle( Document doc )
    {
      AnalysisDisplayStyle analysisDisplayStyle;

      const string styleName
        = "Revit Webcam Display Style";

      // Extract existing display styles with specific name

      FilteredElementCollector a
        = new FilteredElementCollector( doc );

      IList<Element> elements = a
        .OfClass( typeof( AnalysisDisplayStyle ) )
        .Where( x => x.Name.Equals( styleName ) )
        .Cast<Element>()
        .ToList();

      if( 0 < elements.Count )
      {
        // Use the existing display style

        analysisDisplayStyle = elements[0]
          as AnalysisDisplayStyle;
      }
      else
      {
        // Create new display style:

        // Coloured surface settings:

        AnalysisDisplayColoredSurfaceSettings
          coloredSurfaceSettings
            = new AnalysisDisplayColoredSurfaceSettings();

        coloredSurfaceSettings.ShowGridLines = false;

        // Colour settings:

        AnalysisDisplayColorSettings colorSettings
          = new AnalysisDisplayColorSettings();

        colorSettings.MaxColor = new Color( 255, 255, 255 );
        colorSettings.MinColor = new Color( 0, 0, 0 );

        // Legend settings:

        AnalysisDisplayLegendSettings legendSettings
          = new AnalysisDisplayLegendSettings();

        legendSettings.NumberOfSteps = 10;
        legendSettings.Rounding = 0.05;
        legendSettings.ShowDataDescription = false;
        legendSettings.ShowLegend = true;

        //// Extract legend text:

        //a = new FilteredElementCollector( doc );

        //elements = a
        //  .OfClass( typeof( TextNoteType ) )
        //  .Where( x => x.Name == "LegendText" )
        //  .Cast<Element>()
        //  .ToList();

        //if( 0 < elements.Count )
        //{
        //  // if LegendText exists, use it for this display style

        //  TextNoteType textType = elements[0] as TextNoteType;

        //  // warning CS0618: 
        //  // SetTextTypeId(ElementId, Document) is obsolete: 
        //  // this method will be obsolete from 2012.

        //  legendSettings.SetTextTypeId( textType.Id, doc );
        //}

        // Create the analysis display style:

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
        View view = doc.ActiveView;

        Reference r = uidoc.Selection.PickObject(
          ObjectType.Face,
          new BimElementFilter(),
          _prompt );

        Debug.Assert( null != r,
          "expected non-null reference from PickObject" );

        ElementId id = r.ElementId;

        Debug.Assert( null != doc.GetElement( id ),
          "expected valid element from PickObject" ); // 2013

        Debug.Assert( null != doc.GetElement( id )
          .GetGeometryObjectFromReference(
            r ) as Face,
          "expected non-null geometry object from PickObject" ); // 2013

        SetAnalysisDisplayStyle( doc );

        //uiapp.Idling
        //  += new EventHandler<IdlingEventArgs>(
        //    OnIdling );

        WebcamEventHandler.Start( view, r );

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
