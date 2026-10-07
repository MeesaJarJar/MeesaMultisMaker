using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using MeesaMultisMaker.Generation;

namespace MeesaMultisMaker
{
    public partial class Form1
    {
        /// <summary>
        /// Adds current canvas objects as a training example
        /// </summary>
        private void AddCurrentAsTrainingData()
        {
       if (placedObjects.Count == 0)
            {
    outputTextBox.AppendText("Canvas is empty. Place some objects first.\r\n");
              return;
    }

            // Deep copy current structure
      var snapshot = placedObjects.Select(o => new PlacedObject
            {
  Image = o.Image,
       GraphicId = o.GraphicId,
  GridX = o.GridX,
                GridY = o.GridY,
   Z = o.Z,
          Flags = o.Flags,
           IsoPosition = o.IsoPosition,
       Layer = o.Layer,
                Hidden = o.Hidden
            }).ToList();

         trainingStructures.Add(snapshot);

            outputTextBox.AppendText($"Added structure as training example #{trainingStructures.Count} " +
      $"(Objects: {snapshot.Count}, Total examples: {trainingStructures.Count})\r\n");
        }

        /// <summary>
        /// Add a multi structure as training data from text format.
        /// Called from MulViewer to bridge multi.mul data into generation pipeline.
        /// </summary>
        public void AddMultiTraining(string multiText)
        {
            if (string.IsNullOrWhiteSpace(multiText))
                return;

            var snapshot = new List<PlacedObject>();
            var lines = multiText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#"))
                    continue;

                var parts = trimmed.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 4)
                    continue;

                string graphicId = parts[0];
                int px, py, pz, flags = 0;
                if (!int.TryParse(parts[1], out px)) continue;
                if (!int.TryParse(parts[2], out py)) continue;
                if (!int.TryParse(parts[3], out pz)) continue;
                if (parts.Length >= 5)
                    int.TryParse(parts[4], out flags);

                snapshot.Add(new PlacedObject
                {
                    GraphicId = graphicId,
                    GridX = px,
                    GridY = py,
                    Z = pz,
                    Flags = flags,
                    Layer = snapshot.Count
                });
            }

            if (snapshot.Count > 0)
            {
                trainingStructures.Add(snapshot);
                outputTextBox.AppendText($"Added multi as training example #{trainingStructures.Count} " +
                    $"(Objects: {snapshot.Count}, Total examples: {trainingStructures.Count})\r\n");
            }
        }

        /// <summary>
        /// Clears all training data
     /// </summary>
        private void ClearTrainingData()
     {
            if (trainingStructures.Count == 0)
    {
     outputTextBox.AppendText("No training data to clear.\r\n");
  return;
     }

      var result = MessageBox.Show(
           $"Clear all {trainingStructures.Count} training examples?",
       "Clear Training Data",
                MessageBoxButtons.YesNo,
    MessageBoxIcon.Question);

  if (result == DialogResult.Yes)
    {
    trainingStructures.Clear();
             outputTextBox.AppendText("Training data cleared.\r\n");
    }
        }

        /// <summary>
    /// Generate a new variant structure
        /// </summary>
      private void GenerateVariant()
        {
     if (trainingStructures.Count == 0)
        {
     outputTextBox.AppendText("No training data available.\r\n" +
        "To use generation:\r\n" +
      "1. Create or import a multi structure\r\n" +
   "2. Click 'Add as Training'\r\n" +
         "3. Optionally add more examples\r\n" +
         "4. Click 'Generate Variant'\r\n");
           return;
            }

            // Show generation options dialog (non-modal, stays open)
     var dialog = new GenerationOptionsDialog(trainingStructures);

             // Subscribe to generation event
  dialog.GenerateRequested += () =>
  {
 try
  {
        // Wire up TileData for building-aware classification
       Generation.TileDataLookup.SetReader(tileDataReader);

        // Analyze patterns
       var analyzer = new PatternAnalyzer();
     var library = analyzer.AnalyzeStructures(trainingStructures);

    // DIVERSITY FIX: Randomly select a template from training examples
       // This ensures each generation can use a different base structure
   var random = new Random();
   int templateIndex = random.Next(trainingStructures.Count);
            var templateZones = analyzer.AnalyzeZones(trainingStructures[templateIndex]);

            // Determine grid size
       int width = dialog.UseSourceSize ? templateZones.Width : dialog.GridWidth;
        int height = dialog.UseSourceSize ? templateZones.Height : dialog.GridHeight;

   // Generate
      var generator = new StructureGenerator(library);
         var generatedTiles = generator.Generate(width, height, templateZones, dialog.Randomness);

        if (generatedTiles.Count == 0)
              {
            outputTextBox.AppendText("Generation failed after multiple retries.\r\n" +
  "Try:\r\n" +
   "- Adding more training examples\r\n" +
        "- Reducing randomness\r\n" +
       "- Using source size\r\n");
  return;
      }

        // Validate generated structure against UO multi rules
        var validation = MultiValidator.Validate(generatedTiles);
        if (!validation.IsValid || validation.Warnings.Count > 0)
        {
            outputTextBox.AppendText("── Multi Validation ──\r\n");
            foreach (var err in validation.Errors)
                outputTextBox.AppendText($"  ✗ {err}\r\n");
            foreach (var warn in validation.Warnings)
                outputTextBox.AppendText($"  ⚠ {warn}\r\n");
        }

        // Clear canvas and place generated structure
       PushUndo();
        placedObjects.Clear();

       // Resize canvas
  ResizeCanvas(width, height, pushUndo: false);

      // Place tiles
  int layer = 0;
    foreach (var tile in generatedTiles.OrderBy(t => t.Position.Y).ThenBy(t => t.Position.X).ThenBy(t => t.Position.Z))
   {
         string graphicId = $"0x{tile.TileId:X4}";
    string path;

      if (TryResolveIdToPath(graphicId, out path))
 {
          try
     {
            var img = LoadImageUnlocked(path);
     var iso = GridToIso(tile.Position.X, tile.Position.Y);

  placedObjects.Add(new PlacedObject
      {
       Image = img,
        GraphicId = graphicId,
      GridX = tile.Position.X,
    GridY = tile.Position.Y,
  Z = tile.Position.Z,
       Flags = tile.Flags,
  IsoPosition = iso,
        Layer = layer++
         });
       }
            catch { }
}
   }

     RebuildLockLists();
   designPictureBox.Invalidate();

        outputTextBox.AppendText($"Generated structure with {placedObjects.Count} objects! (Template #{templateIndex+1})\r\n" +
       $"Grid: {width}x{height}\r\n" +
    $"Training examples used: {trainingStructures.Count}\r\n" +
       $"Unique tiles: {library.AllowedTiles.Count}\r\n" +
     $"Learned adjacency rules: {library.AllowedAdjacencies.Count}\r\n" +
             $"Total patterns: {library.Patterns.Count}\r\n");
      }
          catch (Exception ex)
        {
        outputTextBox.AppendText($"Error during generation: {ex.Message}\r\n");
    }
     };

            // Show the dialog as non-modal so it stays open
       dialog.Show(this);
    }
    }
}
