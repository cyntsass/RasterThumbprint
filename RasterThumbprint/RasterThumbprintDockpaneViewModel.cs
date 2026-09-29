using ArcGIS.Core.Data;
using ArcGIS.Core.Data.DDL;
using ArcGIS.Core.Data.Raster;
using ArcGIS.Core.Geometry;

using ArcGIS.Desktop.Catalog;
using ArcGIS.Desktop.Core;
using ArcGIS.Desktop.Core.Geoprocessing;
using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Framework.Dialogs;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

namespace RasterThumbprint
{
    internal class RasterThumbprintDockpaneViewModel : DockPane
    {
        private const string DockPaneID =
            "RasterThumbprint_RasterThumbprintDockpane";

        private const string OutputFeatureClassName =
            "RasterThumbprints";


        // =====================================================
        // CONSTRUCTOR
        // =====================================================

        protected RasterThumbprintDockpaneViewModel()
        {
            _selectedMethod = FootprintMethods[0];
        }


        // =====================================================
        // SHOW DOCKPANE
        // =====================================================

        internal static void Show()
        {
            DockPane pane =
                FrameworkApplication.DockPaneManager.Find(DockPaneID);

            if (pane == null)
            {
                MessageBox.Show(
                    "ArcGIS Pro could not find the Raster Thumbprint DockPane.",
                    "Raster Thumbprint");

                return;
            }

            pane.Activate();
        }


        // =====================================================
        // UI
        // =====================================================

        private string _heading = "Raster Thumbprint";

        public string Heading
        {
            get => _heading;
            set => SetProperty(ref _heading, value);
        }


        public List<string> FootprintMethods { get; } =
            new List<string>
            {
                "1 - Raster Extent",
                "2 - Valid Data",
                "3 - Simplified Valid Data"
            };


        private string _selectedMethod;

        public string SelectedMethod
        {
            get => _selectedMethod;

            set
            {
                SetProperty(ref _selectedMethod, value);

                NotifyPropertyChanged(
                    nameof(IsToleranceEnabled));
            }
        }


        public bool IsToleranceEnabled =>
            SelectedMethod == "3 - Simplified Valid Data";


        private string _simplificationTolerance = "10";

        public string SimplificationTolerance
        {
            get => _simplificationTolerance;
            set => SetProperty(ref _simplificationTolerance, value);
        }


        private string _status = "Ready";

        public string Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }


        // =====================================================
        // COMMAND
        // =====================================================

        public ICommand CreateThumbprintsCommand =>
            new RelayCommand(
                async () => await CreateThumbprintsAsync());


        // =====================================================
        // MAIN PROCESS
        // =====================================================

        private async Task CreateThumbprintsAsync()
        {
            try
            {
                int method =
                    GetSelectedMethodNumber();


                if (MapView.Active == null)
                {
                    MessageBox.Show(
                        "There is no active map.",
                        "Raster Thumbprint");

                    return;
                }


                // -------------------------------------------------
                // Get selected raster layers
                // -------------------------------------------------

                var rasterLayers =
                    MapView.Active
                        .GetSelectedLayers()
                        .OfType<RasterLayer>()
                        .ToList();


                if (rasterLayers.Count == 0)
                {
                    MessageBox.Show(
                        "Select one or more raster layers " +
                        "in the Contents pane first.",
                        "Raster Thumbprint");

                    return;
                }


                // -------------------------------------------------
                // Validate Method 3 tolerance before processing
                // -------------------------------------------------

                double simplificationTolerance = 0;


                if (method == 3)
                {
                    bool validTolerance =
                        double.TryParse(
                            SimplificationTolerance,
                            NumberStyles.Float,
                            CultureInfo.CurrentCulture,
                            out simplificationTolerance);


                    // Also accept decimal point regardless of
                    // current Windows regional settings.
                    if (!validTolerance)
                    {
                        validTolerance =
                            double.TryParse(
                                SimplificationTolerance,
                                NumberStyles.Float,
                                CultureInfo.InvariantCulture,
                                out simplificationTolerance);
                    }


                    if (!validTolerance ||
                        simplificationTolerance <= 0)
                    {
                        MessageBox.Show(
                            "Simplification tolerance must be " +
                            "a number greater than zero.",
                            "Raster Thumbprint");

                        return;
                    }
                }


                // -------------------------------------------------
                // Choose output GDB
                // -------------------------------------------------

                var browseDialog =
                    new OpenItemDialog
                    {
                        Title =
                            "Select output File Geodatabase",

                        MultiSelect = false,

                        Filter =
                            ItemFilters.Geodatabases
                    };


                bool? dialogResult =
                    browseDialog.ShowDialog();


                if (dialogResult != true)
                    return;


                Item selectedItem =
                    browseDialog.Items.FirstOrDefault();


                if (selectedItem == null)
                    return;


                string gdbPath =
                    selectedItem.Path;


                // -------------------------------------------------
                // Create / validate catalog
                // -------------------------------------------------

                Status =
                    "Preparing output catalog...";


                await EnsureCatalogExistsAsync(
                    rasterLayers,
                    gdbPath);


                ProcessingResult result =
                    new ProcessingResult();


                int current = 0;


                // =================================================
                // PROCESS EACH RASTER
                // =================================================

                foreach (RasterLayer rasterLayer in rasterLayers)
                {
                    current++;


                    Status =
                        $"Processing {current} of " +
                        $"{rasterLayers.Count}: " +
                        $"{rasterLayer.Name}";


                    RasterInfo rasterInfo =
                        await ReadRasterInfoAsync(
                            rasterLayer);


                    if (rasterInfo == null)
                    {
                        result.Failed++;
                        continue;
                    }


                    // ---------------------------------------------
                    // Duplicate key:
                    //
                    // RasterPath + Method
                    // ---------------------------------------------

                    bool alreadyExists =
                        await CatalogEntryExistsAsync(
                            gdbPath,
                            rasterInfo.RasterPath,
                            method);


                    if (alreadyExists)
                    {
                        result.Skipped++;
                        continue;
                    }


                    Polygon footprint = null;


                    // =============================================
                    // METHOD 1
                    // Raster Extent
                    // =============================================

                    if (method == 1)
                    {
                        footprint =
                            await CreateExtentGeometryAsync(
                                rasterLayer);
                    }


                    // =============================================
                    // METHOD 2
                    // Valid Data
                    // =============================================

                    else if (method == 2)
                    {
                        footprint =
                            await CreateValidDataGeometryAsync(
                                rasterLayer,
                                gdbPath);
                    }


                    // =============================================
                    // METHOD 3
                    // Simplified Valid Data
                    // =============================================

                    else if (method == 3)
                    {
                        footprint =
                            await CreateSimplifiedValidDataGeometryAsync(
                                rasterLayer,
                                gdbPath,
                                simplificationTolerance);
                    }


                    if (footprint == null ||
                        footprint.IsEmpty)
                    {
                        result.Failed++;
                        continue;
                    }


                    // ---------------------------------------------
                    // Insert one raster = one feature = one row
                    // ---------------------------------------------

                    await InsertCatalogEntryAsync(
                        gdbPath,
                        rasterInfo,
                        footprint,
                        method);


                    result.Created++;
                }


                // -------------------------------------------------
                // Add catalog layer to map
                // -------------------------------------------------

                await AddCatalogToMapAsync(
                    gdbPath);


                Status =
                    $"Created: {result.Created} | " +
                    $"Skipped: {result.Skipped} | " +
                    $"Failed: {result.Failed}";


                string toleranceText =
                    method == 3
                    ? $"\nTolerance: {simplificationTolerance}"
                    : "";


                MessageBox.Show(
                    $"Raster thumbprints complete.\n\n" +
                    $"Created: {result.Created}\n" +
                    $"Skipped existing: {result.Skipped}\n" +
                    $"Failed: {result.Failed}\n\n" +
                    $"Method: {method}" +
                    toleranceText +
                    $"\n\nOutput:\n" +
                    $"{gdbPath}\\{OutputFeatureClassName}",
                    "Raster Thumbprint");
            }
            catch (Exception ex)
            {
                Status = "Error";

                MessageBox.Show(
                    ex.ToString(),
                    "Raster Thumbprint - Error");
            }
        }


        // =====================================================
        // METHOD NUMBER
        // =====================================================

        private int GetSelectedMethodNumber()
        {
            if (SelectedMethod.StartsWith("1"))
                return 1;

            if (SelectedMethod.StartsWith("2"))
                return 2;

            if (SelectedMethod.StartsWith("3"))
                return 3;

            return 1;
        }


        // =====================================================
        // CREATE / VALIDATE OUTPUT CATALOG
        // =====================================================

        private async Task EnsureCatalogExistsAsync(
            List<RasterLayer> rasterLayers,
            string gdbPath)
        {
            await QueuedTask.Run(() =>
            {
                using Geodatabase geodatabase =
                    new Geodatabase(
                        new FileGeodatabaseConnectionPath(
                            new Uri(gdbPath)));


                bool exists = false;


                try
                {
                    using FeatureClass test =
                        geodatabase.OpenDataset<FeatureClass>(
                            OutputFeatureClassName);

                    exists = true;
                }
                catch
                {
                    exists = false;
                }


                if (exists)
                {
                    ValidateCatalogSchema(
                        geodatabase);

                    return;
                }


                // -------------------------------------------------
                // Output CRS = CRS of first selected raster
                // -------------------------------------------------

                Envelope firstExtent =
                    rasterLayers[0].QueryExtent();


                if (firstExtent == null ||
                    firstExtent.IsEmpty)
                {
                    throw new Exception(
                        "Could not determine the extent " +
                        "of the first selected raster.");
                }


                SpatialReference spatialReference =
                    firstExtent.SpatialReference;


                if (spatialReference == null)
                {
                    throw new Exception(
                        "The first selected raster does not " +
                        "have a valid spatial reference.");
                }


                ShapeDescription shapeDescription =
                    new ShapeDescription(
                        GeometryType.Polygon,
                        spatialReference);


                var fields =
                    new List<ArcGIS.Core.Data.DDL.FieldDescription>
                    {
                        new ArcGIS.Core.Data.DDL.FieldDescription(
                            "RasterName",
                            FieldType.String)
                        {
                            Length = 255
                        },

                        new ArcGIS.Core.Data.DDL.FieldDescription(
                            "RasterPath",
                            FieldType.String)
                        {
                            Length = 2000
                        },

                        new ArcGIS.Core.Data.DDL.FieldDescription(
                            "Method",
                            FieldType.SmallInteger),

                        new ArcGIS.Core.Data.DDL.FieldDescription(
                            "PixelSizeX",
                            FieldType.Double),

                        new ArcGIS.Core.Data.DDL.FieldDescription(
                            "PixelSizeY",
                            FieldType.Double),

                        new ArcGIS.Core.Data.DDL.FieldDescription(
                            "Columns",
                            FieldType.Integer),

                        new ArcGIS.Core.Data.DDL.FieldDescription(
                            "Rows",
                            FieldType.Integer)
                    };


                FeatureClassDescription fcDescription =
                    new FeatureClassDescription(
                        OutputFeatureClassName,
                        fields,
                        shapeDescription);


                SchemaBuilder schemaBuilder =
                    new SchemaBuilder(
                        geodatabase);


                schemaBuilder.Create(
                    fcDescription);


                if (!schemaBuilder.Build())
                {
                    string errors =
                        string.Join(
                            Environment.NewLine,
                            schemaBuilder.ErrorMessages);


                    throw new Exception(
                        "Could not create RasterThumbprints.\n\n" +
                        errors);
                }
            });
        }


        // =====================================================
        // VALIDATE EXISTING CATALOG
        // =====================================================

        private static void ValidateCatalogSchema(
            Geodatabase geodatabase)
        {
            using FeatureClass featureClass =
                geodatabase.OpenDataset<FeatureClass>(
                    OutputFeatureClassName);


            FeatureClassDefinition definition =
                featureClass.GetDefinition();


            string[] requiredFields =
            {
                "RasterName",
                "RasterPath",
                "Method",
                "PixelSizeX",
                "PixelSizeY",
                "Columns",
                "Rows"
            };


            foreach (string fieldName in requiredFields)
            {
                if (definition.FindField(fieldName) < 0)
                {
                    throw new Exception(
                        $"RasterThumbprints is missing " +
                        $"'{fieldName}'.\n\n" +
                        "Delete the old RasterThumbprints " +
                        "feature class and run the tool again.");
                }
            }
        }


        // =====================================================
        // READ RASTER METADATA
        // =====================================================

        private async Task<RasterInfo> ReadRasterInfoAsync(
            RasterLayer rasterLayer)
        {
            return await QueuedTask.Run(() =>
            {
                Raster raster =
                    rasterLayer.GetRaster();


                if (raster == null)
                    return null;


                string rasterPath = "";


                try
                {
                    Uri uri =
                        rasterLayer.GetPath();


                    if (uri != null)
                    {
                        rasterPath =
                            uri.IsFile
                            ? uri.LocalPath
                            : uri.ToString();
                    }
                }
                catch
                {
                    rasterPath = "";
                }


                Tuple<double, double> cellSize =
                    raster.GetMeanCellSize();


                return new RasterInfo
                {
                    RasterName =
                        rasterLayer.Name,

                    RasterPath =
                        rasterPath,

                    PixelSizeX =
                        cellSize.Item1,

                    PixelSizeY =
                        cellSize.Item2,

                    Columns =
                        raster.GetWidth(),

                    Rows =
                        raster.GetHeight()
                };
            });
        }


        // =====================================================
        // METHOD 1
        // RASTER EXTENT
        // =====================================================

        private async Task<Polygon> CreateExtentGeometryAsync(
            RasterLayer rasterLayer)
        {
            return await QueuedTask.Run(() =>
            {
                Envelope extent =
                    rasterLayer.QueryExtent();


                if (extent == null ||
                    extent.IsEmpty)
                {
                    return null;
                }


                return PolygonBuilderEx.CreatePolygon(
                    extent);
            });
        }


        // =====================================================
        // METHOD 2
        // VALID DATA
        //
        // RasterDomain_3d
        //       ↓
        // Z-aware polygon
        //       ↓
        // CopyFeatures with outputZFlag=Disabled
        //       ↓
        // clean XY polygon
        // =====================================================

        private async Task<Polygon> CreateValidDataGeometryAsync(
            RasterLayer rasterLayer,
            string gdbPath)
        {
            // -------------------------------------------------
            // Source path for diagnostics
            // -------------------------------------------------

            string rasterPath =
                await QueuedTask.Run(() =>
                {
                    Uri uri =
                        rasterLayer.GetPath();


                    if (uri == null)
                        return null;


                    return uri.IsFile
                        ? uri.LocalPath
                        : uri.ToString();
                });


            if (string.IsNullOrWhiteSpace(
                    rasterPath))
            {
                throw new Exception(
                    $"Could not determine source path for:\n" +
                    $"{rasterLayer.Name}");
            }


            // -------------------------------------------------
            // Unique temporary datasets
            // -------------------------------------------------

            string id =
                Guid.NewGuid()
                    .ToString("N")
                    .Substring(0, 12);


            string domainName =
                "RT_Domain_" + id;


            string flatName =
                "RT_2D_" + id;


            string domainOutput =
                Path.Combine(
                    gdbPath,
                    domainName);


            string flatOutput =
                Path.Combine(
                    gdbPath,
                    flatName);


            bool domainCreated = false;
            bool flatCreated = false;


            try
            {
                // =================================================
                // STEP 1
                // RASTER DOMAIN
                // =================================================

                var domainParameters =
                    Geoprocessing.MakeValueArray(
                        rasterLayer,
                        domainOutput,
                        "POLYGON");


                var domainEnvironment =
                    Geoprocessing.MakeEnvironmentArray(
                        overwriteoutput: true);


                IGPResult domainResult =
                    await Geoprocessing.ExecuteToolAsync(
                        "RasterDomain_3d",
                        domainParameters,
                        domainEnvironment,
                        null,
                        null,
                        GPExecuteToolFlags.GPThread);


                if (domainResult == null)
                {
                    throw new Exception(
                        $"Raster Domain returned no result for:\n" +
                        $"{rasterLayer.Name}");
                }


                if (domainResult.IsFailed)
                {
                    string messages =
                        GetGPMessageText(
                            domainResult);


                    throw new Exception(
                        $"Raster Domain failed for:\n" +
                        $"{rasterLayer.Name}\n\n" +
                        $"Input:\n{rasterPath}\n\n" +
                        $"ArcGIS messages:\n{messages}");
                }


                domainCreated = true;


                // =================================================
                // STEP 2
                // FORCE DOMAIN OUTPUT TO 2D
                // =================================================

                var copyParameters =
                    Geoprocessing.MakeValueArray(
                        domainOutput,
                        flatOutput);


                var copyEnvironment =
                    Geoprocessing.MakeEnvironmentArray(
                        outputZFlag: "Disabled",
                        overwriteoutput: true);


                IGPResult copyResult =
                    await Geoprocessing.ExecuteToolAsync(
                        "management.CopyFeatures",
                        copyParameters,
                        copyEnvironment,
                        null,
                        null,
                        GPExecuteToolFlags.GPThread);


                if (copyResult == null)
                {
                    throw new Exception(
                        $"2D conversion returned no result for:\n" +
                        $"{rasterLayer.Name}");
                }


                if (copyResult.IsFailed)
                {
                    string messages =
                        GetGPMessageText(
                            copyResult);


                    throw new Exception(
                        $"Could not convert Raster Domain " +
                        $"geometry to 2D for:\n" +
                        $"{rasterLayer.Name}\n\n" +
                        $"ArcGIS messages:\n{messages}");
                }


                flatCreated = true;


                // =================================================
                // STEP 3
                // READ 2D POLYGON
                // =================================================

                Polygon polygon =
                    await ReadPolygonFeatureClassAsync(
                        gdbPath,
                        flatName);


                if (polygon == null ||
                    polygon.IsEmpty)
                {
                    throw new Exception(
                        $"No valid 2D footprint was produced for:\n" +
                        $"{rasterLayer.Name}");
                }


                if (polygon.HasZ)
                {
                    throw new Exception(
                        $"The valid-data footprint for " +
                        $"{rasterLayer.Name} is unexpectedly Z-aware.");
                }


                return polygon;
            }
            finally
            {
                if (flatCreated)
                {
                    await DeleteTemporaryDatasetAsync(
                        flatOutput);
                }


                if (domainCreated)
                {
                    await DeleteTemporaryDatasetAsync(
                        domainOutput);
                }
            }
        }


        // =====================================================
        // METHOD 3
        // SIMPLIFIED VALID DATA
        //
        // Method 2
        //    ↓
        // clean 2D valid-data polygon
        //    ↓
        // temporary polygon FC
        //    ↓
        // SimplifyPolygon
        //    ↓
        // simplified XY polygon
        // =====================================================

        private async Task<Polygon> CreateSimplifiedValidDataGeometryAsync(
            RasterLayer rasterLayer,
            string gdbPath,
            double tolerance)
        {
            // -------------------------------------------------
            // Generate the clean valid-data geometry first.
            // This uses the already-working Method 2 pipeline.
            // -------------------------------------------------

            Polygon validDataPolygon =
                await CreateValidDataGeometryAsync(
                    rasterLayer,
                    gdbPath);


            if (validDataPolygon == null ||
                validDataPolygon.IsEmpty)
            {
                return null;
            }


            if (validDataPolygon.HasZ)
            {
                throw new Exception(
                    $"Method 2 returned a Z-aware geometry for " +
                    $"{rasterLayer.Name}.");
            }


            // -------------------------------------------------
            // Temporary datasets
            // -------------------------------------------------

            string id =
                Guid.NewGuid()
                    .ToString("N")
                    .Substring(0, 12);


            string inputName =
                "RT_SimpIn_" + id;


            string outputName =
                "RT_SimpOut_" + id;


            string inputPath =
                Path.Combine(
                    gdbPath,
                    inputName);


            string outputPath =
                Path.Combine(
                    gdbPath,
                    outputName);


            bool inputCreated = false;
            bool outputCreated = false;


            try
            {
                // =================================================
                // STEP 1
                // WRITE VALID-DATA POLYGON TO TEMPORARY FC
                // =================================================

                await QueuedTask.Run(() =>
                {
                    using Geodatabase geodatabase =
                        new Geodatabase(
                            new FileGeodatabaseConnectionPath(
                                new Uri(gdbPath)));


                    ShapeDescription shapeDescription =
                        new ShapeDescription(
                            GeometryType.Polygon,
                            validDataPolygon.SpatialReference);


                    FeatureClassDescription fcDescription =
                        new FeatureClassDescription(
                            inputName,
                            new List<ArcGIS.Core.Data.DDL.FieldDescription>(),
                            shapeDescription);


                    SchemaBuilder schemaBuilder =
                        new SchemaBuilder(
                            geodatabase);


                    schemaBuilder.Create(
                        fcDescription);


                    if (!schemaBuilder.Build())
                    {
                        string errors =
                            string.Join(
                                Environment.NewLine,
                                schemaBuilder.ErrorMessages);


                        throw new Exception(
                            "Could not create temporary " +
                            "simplification feature class.\n\n" +
                            errors);
                    }


                    using FeatureClass featureClass =
                        geodatabase.OpenDataset<FeatureClass>(
                            inputName);


                    using RowBuffer rowBuffer =
                        featureClass.CreateRowBuffer();


                    rowBuffer["SHAPE"] =
                        validDataPolygon;


                    using Row row =
                        featureClass.CreateRow(
                            rowBuffer);
                });


                inputCreated = true;


                // =================================================
                // STEP 2
                // SIMPLIFY POLYGON
                // =================================================

                var simplifyParameters =
                    Geoprocessing.MakeValueArray(
                        inputPath,
                        outputPath,
                        "POINT_REMOVE",
                        tolerance);


                var simplifyEnvironment =
                    Geoprocessing.MakeEnvironmentArray(
                        outputZFlag: "Disabled",
                        overwriteoutput: true);


                IGPResult simplifyResult =
                    await Geoprocessing.ExecuteToolAsync(
                        "cartography.SimplifyPolygon",
                        simplifyParameters,
                        simplifyEnvironment,
                        null,
                        null,
                        GPExecuteToolFlags.GPThread);


                if (simplifyResult == null)
                {
                    throw new Exception(
                        $"Simplify Polygon returned no result for:\n" +
                        $"{rasterLayer.Name}");
                }


                if (simplifyResult.IsFailed)
                {
                    string messages =
                        GetGPMessageText(
                            simplifyResult);


                    throw new Exception(
                        $"Simplify Polygon failed for:\n" +
                        $"{rasterLayer.Name}\n\n" +
                        $"Tolerance: {tolerance}\n\n" +
                        $"ArcGIS messages:\n{messages}");
                }


                outputCreated = true;


                // =================================================
                // STEP 3
                // READ SIMPLIFIED GEOMETRY
                // =================================================

                Polygon simplifiedPolygon =
                    await ReadPolygonFeatureClassAsync(
                        gdbPath,
                        outputName);


                if (simplifiedPolygon == null ||
                    simplifiedPolygon.IsEmpty)
                {
                    throw new Exception(
                        $"Simplification produced no polygon for:\n" +
                        $"{rasterLayer.Name}");
                }


                if (simplifiedPolygon.HasZ)
                {
                    throw new Exception(
                        $"Simplified footprint unexpectedly " +
                        $"contains Z values for:\n" +
                        $"{rasterLayer.Name}");
                }


                return simplifiedPolygon;
            }
            finally
            {
                if (outputCreated)
                {
                    await DeleteTemporaryDatasetAsync(
                        outputPath);
                }


                if (inputCreated)
                {
                    await DeleteTemporaryDatasetAsync(
                        inputPath);
                }
            }
        }


        // =====================================================
        // READ POLYGON FEATURE CLASS
        //
        // If ArcGIS returns multiple features, union them so:
        //
        // 1 raster = 1 polygon geometry = 1 catalog row
        // =====================================================

        private async Task<Polygon> ReadPolygonFeatureClassAsync(
            string gdbPath,
            string featureClassName)
        {
            return await QueuedTask.Run(() =>
            {
                using Geodatabase geodatabase =
                    new Geodatabase(
                        new FileGeodatabaseConnectionPath(
                            new Uri(gdbPath)));


                using FeatureClass featureClass =
                    geodatabase.OpenDataset<FeatureClass>(
                        featureClassName);


                List<Geometry> geometries =
                    new List<Geometry>();


                using RowCursor cursor =
                    featureClass.Search(
                        null,
                        false);


                while (cursor.MoveNext())
                {
                    using Feature feature =
                        cursor.Current as Feature;


                    if (feature == null)
                        continue;


                    Geometry geometry =
                        feature.GetShape();


                    if (geometry != null &&
                        !geometry.IsEmpty)
                    {
                        geometries.Add(
                            geometry);
                    }
                }


                if (geometries.Count == 0)
                    return null;


                if (geometries.Count == 1)
                {
                    return geometries[0]
                        as Polygon;
                }


                return GeometryEngine.Instance.Union(
                    geometries)
                    as Polygon;
            });
        }


        // =====================================================
        // DELETE TEMPORARY DATASET
        // =====================================================

        private async Task DeleteTemporaryDatasetAsync(
            string datasetPath)
        {
            try
            {
                var parameters =
                    Geoprocessing.MakeValueArray(
                        datasetPath);


                await Geoprocessing.ExecuteToolAsync(
                    "management.Delete",
                    parameters,
                    null,
                    null,
                    null,
                    GPExecuteToolFlags.GPThread);
            }
            catch
            {
                // Cleanup failure should not invalidate
                // an otherwise successful footprint.
            }
        }


        // =====================================================
        // GP MESSAGE HELPER
        // =====================================================

        private static string GetGPMessageText(
            IGPResult result)
        {
            if (result == null ||
                result.Messages == null)
            {
                return "(no ArcGIS messages)";
            }


            string text =
                string.Join(
                    Environment.NewLine,
                    result.Messages.Select(
                        message =>
                            $"{message.Type}: {message.Text}"));


            return string.IsNullOrWhiteSpace(text)
                ? "(no ArcGIS messages)"
                : text;
        }


        // =====================================================
        // DUPLICATE CHECK
        //
        // Unique identity:
        //
        // RasterPath + Method
        // =====================================================

        private async Task<bool> CatalogEntryExistsAsync(
            string gdbPath,
            string rasterPath,
            int method)
        {
            if (string.IsNullOrWhiteSpace(
                    rasterPath))
            {
                return false;
            }


            string normalizedTarget =
                NormalizePath(
                    rasterPath);


            return await QueuedTask.Run(() =>
            {
                using Geodatabase geodatabase =
                    new Geodatabase(
                        new FileGeodatabaseConnectionPath(
                            new Uri(gdbPath)));


                using FeatureClass featureClass =
                    geodatabase.OpenDataset<FeatureClass>(
                        OutputFeatureClassName);


                QueryFilter query =
                    new QueryFilter
                    {
                        SubFields =
                            "RasterPath, Method"
                    };


                using RowCursor cursor =
                    featureClass.Search(
                        query,
                        false);


                while (cursor.MoveNext())
                {
                    using Row row =
                        cursor.Current;


                    object pathValue =
                        row["RasterPath"];

                    object methodValue =
                        row["Method"];


                    if (pathValue == null ||
                        pathValue == DBNull.Value ||
                        methodValue == null ||
                        methodValue == DBNull.Value)
                    {
                        continue;
                    }


                    string existingPath =
                        NormalizePath(
                            pathValue.ToString());


                    int existingMethod =
                        Convert.ToInt32(
                            methodValue);


                    if (existingMethod == method &&
                        string.Equals(
                            existingPath,
                            normalizedTarget,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }


                return false;
            });
        }


        // =====================================================
        // INSERT INTO CATALOG
        // =====================================================

        private async Task InsertCatalogEntryAsync(
            string gdbPath,
            RasterInfo rasterInfo,
            Polygon footprint,
            int method)
        {
            await QueuedTask.Run(() =>
            {
                using Geodatabase geodatabase =
                    new Geodatabase(
                        new FileGeodatabaseConnectionPath(
                            new Uri(gdbPath)));


                using FeatureClass featureClass =
                    geodatabase.OpenDataset<FeatureClass>(
                        OutputFeatureClassName);


                FeatureClassDefinition definition =
                    featureClass.GetDefinition();


                SpatialReference outputSpatialReference =
                    definition.GetSpatialReference();


                Polygon outputPolygon =
                    footprint;


                // -------------------------------------------------
                // Safety: catalog is 2D
                // -------------------------------------------------

                if (outputPolygon.HasZ)
                {
                    throw new Exception(
                        $"Footprint for {rasterInfo.RasterName} " +
                        "is unexpectedly Z-aware.");
                }


                // -------------------------------------------------
                // Project to catalog CRS if necessary
                // -------------------------------------------------

                if (outputPolygon.SpatialReference != null &&
                    outputSpatialReference != null &&
                    !outputPolygon.SpatialReference.IsEqual(
                        outputSpatialReference))
                {
                    outputPolygon =
                        GeometryEngine.Instance.Project(
                            outputPolygon,
                            outputSpatialReference)
                        as Polygon;


                    if (outputPolygon == null)
                    {
                        throw new Exception(
                            $"Could not project footprint for " +
                            $"{rasterInfo.RasterName}.");
                    }
                }


                // -------------------------------------------------
                // Final dimensionality check
                // -------------------------------------------------

                if (outputPolygon.HasZ)
                {
                    throw new Exception(
                        $"Projected footprint for " +
                        $"{rasterInfo.RasterName} " +
                        "unexpectedly contains Z values.");
                }


                // -------------------------------------------------
                // Write one catalog row
                // -------------------------------------------------

                using RowBuffer rowBuffer =
                    featureClass.CreateRowBuffer();


                rowBuffer["SHAPE"] =
                    outputPolygon;

                rowBuffer["RasterName"] =
                    rasterInfo.RasterName;

                rowBuffer["RasterPath"] =
                    rasterInfo.RasterPath;

                rowBuffer["Method"] =
                    (short)method;

                rowBuffer["PixelSizeX"] =
                    rasterInfo.PixelSizeX;

                rowBuffer["PixelSizeY"] =
                    rasterInfo.PixelSizeY;

                rowBuffer["Columns"] =
                    rasterInfo.Columns;

                rowBuffer["Rows"] =
                    rasterInfo.Rows;


                using Row row =
                    featureClass.CreateRow(
                        rowBuffer);
            });
        }


        // =====================================================
        // ADD CATALOG TO MAP
        // =====================================================

        private async Task AddCatalogToMapAsync(
            string gdbPath)
        {
            await QueuedTask.Run(() =>
            {
                bool alreadyPresent =
                    MapView.Active.Map
                        .GetLayersAsFlattenedList()
                        .Any(layer =>
                            string.Equals(
                                layer.Name,
                                OutputFeatureClassName,
                                StringComparison.OrdinalIgnoreCase));


                if (alreadyPresent)
                    return;


                Uri featureClassUri =
                    new Uri(
                        Path.Combine(
                            gdbPath,
                            OutputFeatureClassName));


                LayerFactory.Instance.CreateLayer(
                    featureClassUri,
                    MapView.Active.Map);
            });
        }


        // =====================================================
        // NORMALIZE PATH
        // =====================================================

        private static string NormalizePath(
            string path)
        {
            if (string.IsNullOrWhiteSpace(
                    path))
            {
                return "";
            }


            try
            {
                return Path.GetFullPath(
                        path)
                    .TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return path.Trim();
            }
        }


        // =====================================================
        // DATA CLASSES
        // =====================================================

        private class RasterInfo
        {
            public string RasterName { get; set; }

            public string RasterPath { get; set; }

            public double PixelSizeX { get; set; }

            public double PixelSizeY { get; set; }

            public int Columns { get; set; }

            public int Rows { get; set; }
        }


        private class ProcessingResult
        {
            public int Created { get; set; }

            public int Skipped { get; set; }

            public int Failed { get; set; }
        }
    }


    // =========================================================
    // RIBBON BUTTON
    // =========================================================

    internal class RasterThumbprintDockpane_ShowButton : Button
    {
        protected override void OnClick()
        {
            RasterThumbprintDockpaneViewModel.Show();
        }
    }
}