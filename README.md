# SynoThumbnail Generator
DS PhotoStation is a media browsing, search and asset management application for Synology NASes.
It works via the Synology indexing service, which runs through all photos in a given folder
(usually /photo) and indexes the metadata of all photos, as well as creating various thumbnails
for fast previewing of images across the LAN/Web.

However, there are some problems with the Synology indexing, namely that it's slow. It uses
ImageMagick (IM) to create the thumbs, but it seems like it runs a conversion for each thumbnail in
turn, each time reading the source image, meaning that for a 7MB JPEG, it'll read 35MB off the
disk in order to create the thumbnails. This means it's slow. By leveraging IM's ability to chain
operations, we can load the image once, and then process the output for each thumb, meaning it's
far quicker. 

In the process, we can also fix another Synology indexing bug - where PhotoStation deos not
respect the image's EXIF rotation metadata, meaning that images get displayed in PhotoStation 
with the wrong rotation. By using IM's -auto-orient when generating the thumbs, we can get
them the right way around. 

### Running the tool

The utility is a .Net/C# application, developed with Visual Studio fo Mac. To run it on Synology, copy
the EXE over to a folder and in terminal, run it, passing in the root folder to process.

```
    mono SynoThumbnailGen.exe /volume1/photo 
```

#### Arguments

```
-r     will force the tool to process all subdirs recursively. You probably want this. 
-v     will turn on verbose logging.
-alpha will process folders in alphabetic order (default is most-recent-first)
-gm    will use GraphicsMagick (if installed) which converts about twice as fast as IM
```
### Disclaimer

I accept no liability for any data loss or corruption caused by the use of this application. Your 
use of this app is entirely at your own risk - please ensure that you have adequate backups before
you use this software.

Software (C) Copyright 2018 Mark Otway

### Credits

Michael Medin, from whom the original inspiration for this tool first arose:
https://www.medin.name/blog/2012/04/22/thumbnail-1000s-of-photos-on-a-synology-nas-in-hours-not-months/

Nick Veenhof, who took Michael's idea and developed it further with C++.
https://nickveenhof.be/blog/speed-thumbnail-generation-synology-ds212j

Mark Setchell for his detailed profiling of multi-thumbnail conversion in ImageMagick: https://stackoverflow.com/questions/23748610/fastest-way-to-create-multiple-thumbnails-from-a-single-large-image-in-python

Paul Barrett - for investigating the EXIF issue in Synology's PhotoStation app.
https://forum.synology.com/enu/viewtopic.php?t=127389

