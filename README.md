# Raster Thumbprint ArcGIS Pro addIn

Raster Thumbprint is an ArcGIS Pro add-in for generating polygon footprints from raster datasets.

It can create simple raster extents, valid-data footprints that account for NoData, and simplified valid-data footprints. Multiple selected raster layers can be processed into a single feature class in a File Geodatabase.

Developed at the University of Gothenburg.

## TL;DR

You only need the RasterThumbprint.esriAddInX file to install Raster Thumbprint.
Download the .esriAddInX file → double-click it → click Install Add-In → open/restart ArcGIS Pro.
Raster Thumbprint will then appear under the Add-In tab.

------------------------------------------------------------

## Features

- Process multiple selected raster layers at once
- One raster = one polygon feature = one table row
- File Geodatabase (`.gdb`) output
- Duplicate detection using raster path and footprint method
- Automatic projection to the output feature class coordinate system
- Stores useful raster metadata
- Supports multipart valid-data footprints
- Three footprint methods

---

## Footprint Methods

### 1 — Raster Extent

Creates a simple rectangular polygon representing the raster extent.

This is the fastest method.

Note that the extent is an axis-aligned bounding rectangle and therefore does not necessarily follow the orientation or valid-data boundary of a rotated raster.

### 2 — Valid Data

Creates a polygon representing the actual valid-data area of the raster.

NoData areas around the raster are excluded.

This method uses ArcGIS Pro's **Raster Domain** geoprocessing tool.

Disconnected valid-data regions are retained as a multipart polygon so that each raster is still represented by a single feature.

### 3 — Simplified Valid Data

Creates a valid-data footprint and simplifies its polygon boundary using a user-defined tolerance.

The tolerance is specified in the map/coordinate-system units.

This method is useful when exact valid-data boundaries contain more vertices than are necessary for a raster catalogue or overview map.

---

## Output

The add-in creates a polygon feature class named:

`RasterThumbprints`

inside a user-selected File Geodatabase.

Each raster produces one feature for the selected footprint method.

The same raster can therefore have separate entries for different methods.

Example:

| RasterName | Method |
|---|---:|
| CTX_Image_001.tif | 1 |
| CTX_Image_001.tif | 2 |
| CTX_Image_001.tif | 3 |

Duplicate entries using the same raster path and method are skipped.

---

## Stored Raster Information

The output feature class contains:

| Field | Description |
|---|---|
| `RasterName` | Raster layer name |
| `RasterPath` | Source raster path |
| `Method` | Footprint method (1, 2, or 3) |
| `PixelSizeX` | Raster pixel size in X |
| `PixelSizeY` | Raster pixel size in Y |
| `Columns` | Number of raster columns |
| `Rows` | Number of raster rows |
| `Shape` | Polygon footprint |

ArcGIS also maintains its standard geometry fields such as object ID, shape area, and shape length.

---

## Requirements

- ArcGIS Pro 3.7
- Windows
- ArcGIS Pro SDK-compatible installation

### 3D Analyst

Methods **2 — Valid Data** and **3 — Simplified Valid Data** use the ArcGIS **Raster Domain** tool and therefore require the **3D Analyst** extension.

Method **1 — Raster Extent** does not require Raster Domain.

---

## Installation

Download the latest:

`RasterThumbprint.esriAddInX`

from the GitHub Releases page.

Double-click the file and install the ArcGIS Pro add-in.

Start or restart ArcGIS Pro.

The tool will appear under the **Add-In** tab as:

**Raster Thumbprint**



---

## Usage

1. Open an ArcGIS Pro project.
2. Add the raster datasets you want to process.
3. Select one or more raster layers in the **Contents** pane.
4. Open **Raster Thumbprint** from the **Add-In** tab.
5. Choose a footprint method.
6. For Method 3, enter a simplification tolerance.
7. Create a File Geodatabase (`.gdb`) for the output.
8. Click **CREATE THUMBPRINTS**.
9. Select the output File Geodatabase.
10. After processing, add the `RasterThumbprints` feature class from the geodatabase to the map if necessary.

---

## Simplification Tolerance

The simplification tolerance used by Method 3 is expressed in the coordinate-system units of the footprint.

For example, for a projected coordinate system using metres:

- `10` = 10 metres
- `50` = 50 metres
- `100` = 100 metres
- `1000` = 1 kilometre

The appropriate value depends on raster resolution, footprint complexity, coordinate system, and intended map scale.

---

## How It Works

### Method 1

    Raster
      |
      v
    Raster Extent
      |
      v
    Polygon
      |
      v
    RasterThumbprints

### Method 2

    Raster
      |
      v
    Raster Domain
      |
      v
    Valid-data polygon
      |
      v
    Convert to 2D
      |
      v
    RasterThumbprints

### Method 3

    Raster
      |
      v
    Raster Domain
      |
      v
    Valid-data polygon
      |
      v
    Convert to 2D
      |
      v
    Simplify Polygon
      |
      v
    RasterThumbprints

---

## Building From Source

Raster Thumbprint is written in C# using the ArcGIS Pro SDK for .NET.

Development requirements include:

- Visual Studio
- .NET 10 SDK
- ArcGIS Pro 3.7
- ArcGIS Pro SDK for .NET

Clone the repository and open the Visual Studio solution.

For development:

`F5`

builds the add-in and launches ArcGIS Pro.

For distribution, switch Visual Studio from **Debug** to **Release** and rebuild the solution.

The resulting `.esriAddInX` package can then be distributed to ArcGIS Pro users.

---

## Project Structure

Typical project files include:

    RasterThumbprint/
    |
    |-- Config.daml
    |-- Module1.cs
    |-- RasterThumbprintDockpane.xaml
    |-- RasterThumbprintDockpane.xaml.cs
    |-- RasterThumbprintDockpaneViewModel.cs
    |-- RasterThumbprint.csproj
    |
    |-- Images/
    `-- DarkImages/

### Config.daml

Defines the ArcGIS Pro add-in metadata, ribbon button, DockPane, captions, and icons.

### RasterThumbprintDockpane.xaml

Defines the Raster Thumbprint user interface.

### RasterThumbprintDockpaneViewModel.cs

Contains the main footprint-generation logic, raster metadata handling, duplicate checking, geoprocessing calls, and geodatabase output.

---

## Current Limitations

- Valid-data footprint generation requires 3D Analyst.
- Raster Domain processing can take significantly longer than simple raster extents for large rasters.
- Output is currently written to a File Geodatabase.
- The output File Geodatabase must exist before running the tool.
- The add-in currently creates/uses a feature class named `RasterThumbprints`.

---

## Author

Cynthia Sassenroth, Department of Earth Sciences, University of Gothenburg, Sweden

---

## Cat

The cat is important.

**Raster Thumbprint — Makes Nice Boxes.** 🐱
