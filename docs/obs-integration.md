# Set up an OBS capture with ExactFrame

**Send to OBS is available in ExactFrame 2.1.0 and later.**

Use this integration to crop an existing OBS **Display Capture** to your ExactFrame area, then position it on the OBS canvas. You can use the main frame or an additional frame, such as a vertical crop.

## Connect once

1. In **OBS Studio 28 or later**, open **Tools → WebSocket Server Settings**.
2. Enable the WebSocket server and keep authentication enabled.
3. In ExactFrame, select **Send to OBS**.
4. Enter the server port, usually **4455**, and the password shown in OBS, then connect.

ExactFrame connects to OBS on the same PC. The password is cleared after connecting and is never saved.

## Preview and apply a frame

1. Set up your frame in ExactFrame.
2. In **Send to OBS**, choose that frame, an OBS scene and an existing **Display Capture** source placed directly in the scene.
3. Choose how it should fit:

   | Option | Result |
   |---|---|
   | **Fit to canvas** | Scales the crop proportionally to fit. Different aspect ratios leave space around it. |
   | **Actual pixels** | Centers the crop without scaling it. |

4. Select **Preview change**. Review the crop, scale, position and OBS output dimensions. The drawing shows the proposed geometry; it is not a captured image.
5. Stop recording and streaming before selecting **Apply to OBS**. Check the OBS preview, then save a short test clip before a full recording.

The Display Capture item must be **visible, unlocked and unfiltered**. ExactFrame identifies its Windows monitor and converts the frame's desktop coordinates into source-pixel crop margins. Displays with negative origins and different Windows scaling are handled in physical pixels. Unknown monitors and mismatched source dimensions are rejected.

Applying rechecks the source and verifies its resulting transform. It changes that scene item's crop and placement, resetting rotation, flip and bounding box. Your OBS video settings, active scene and other sources are left as they are. Recording and streaming remain under your control.

The output dimensions shown are OBS's global output setting. An encoder's separate recording rescale option can change the final file dimensions, so check the saved recording as well.

## Undo or make another change

Stop recording and streaming before undoing. **Undo last apply** restores the previous crop and transform while the connection remains open. If you have since edited the item in OBS, ExactFrame refuses to overwrite those edits.

Closing the dialog keeps the connection and undo history available. Disconnecting or quitting ExactFrame clears them.

Changing a frame invalidates a prepared preview. Open the dialog and preview again before applying; frame changes are not sent to OBS automatically.

## Current limits

This first integration supports Display Capture items placed directly in a scene. Window Capture, grouped sources, enabled source filters and remote OBS instances are not supported.

A successful apply verifies the OBS transform. Follow the [recording compatibility guide](recording-compatibility.md) to check the actual clip.

[Back to the README](../README.md)
