# SAMCS
A port of https://github.com/s-macke/SAM for netstandard2.0 C#

> *Developer's Note:*<br/>
> Just so you know the code is messy from origin. I tried to remove/simplify some stuff but it is what it is.<br/>
> Just know that this thing works quite well for most needs.

# Compiling
Just run `dotnet build` in the root of this repository to build the library.

# Usage
> **Please note that SAM can only output voice for English ASCII text!!**

To use this library first add the necessary `using` directive like so:
```cs
using SAMCS;
```

The only class you'll need to care about is `SAMCS.SoftwareAutomaticMouth` since it encapsulates
all the data and functions you need in order to generate audio data.

Creating multiple objects of `SoftwareAutomaticMouth` is allowed and no object will interfere with
the values of another object.

To generate audio use the `SoftwareAutomaticMouth` class like in the following example:
```cs
SoftwareAutomaticMouth sam = new SoftwareAutomaticMouth();
AudioBuffer buffer = sam.Speak("Hello!");
```

The `Speak` function takes in two parameters, `string input` is the input text and `bool phonetic`
specifies whether text phonemes should be generated or not (it is adviced to leave `phonetic` at
it's default value of `false`). As a result the `Speak` function returns an `AudioBuffer` object.
`AudioBuffer` is also part of the `SAMCS` package and contains all the output data that the `Speak`
function provides.
***Note*: SAM only outputs 8-bit mono PCM sound data at 22050 Hz**
The buffer is only as big as needed and will not contain any end silence.
To access the buffer data simply access the `Buffer` field within the object. The `Size` field
within `AudioBuffer` describes how big the actual buffer data is.
Example for accessing the data of a buffer:
```cs
AudioBuffer buffer;
byte[] bufferData = buffer.Buffer;
int bufferSize = buffer.Size;
// do whatever you feel like
```

If you don't specify anything in the constructor, that's fine, since SAM will default it's properties
to some preset values.
You can however also specify the pitch, speed, mouth and throat values and also enable, if SAM should sing!
This can be done either in the constructor or after creating the object.
Here's an example for setting them both in the constructor and after creating the object:
```cs
byte pitch = 64;  // these are all default values
byte speed = 72;  // these are all default values
byte mouth = 128;  // these are all default values
byte throat = 128;  // these are all default values
bool sing = false;  // these are all default values

SoftwareAutomaticMouth sam = new SoftwareAutomaticMouth(pitch, speed, mouth, throat, sing);
sam.Pitch = pitch;
sam.Speed = speed;
sam.Mouth = mouth;
sam.Throat = throat;
sam.Sing = sing;
```
The values can be changed at any time and will take effect for the next `Speak` call!
