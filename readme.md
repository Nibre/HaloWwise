# HaloWwise
HaloWwise allows you to un-pack Wwise \*.pck files (from H4/H5) to get the \*.wem and \*.bnk files they contain, while retaining their location structure within the Pack. It may work with other games' \*.pck files, but this is tailored towards H4/H5.

This is something I threw together a little while back, that should be much faster than other methods of extracting/converting, while also grabbing the Bank info that allows you to trace the sounds to/from their Blam Tag equivalent. It wouldn't be that hard to implement a re-packer/injector in the future, if there's demand for that.
```
Usage:
  HaloWwise.exe <input.pck> <extraction path>
  HaloWwise.exe <input directory (recursive)> <extraction path>

Note: If you place ww2ogg.exe and revorb.exe (with the codebook) in the same Path as
this Exe, it will also attempt to convert the *.wem files to *.ogg during extraction (H5 only)
```
[ww2ogg Download](https://github.com/hcs64/ww2ogg/releases/download/0.24/ww2ogg024.zip)

[Revorb Download](http://yirkha.fud.cz/progs/foobar2000/revorb.exe)

## Finding Sounds

To make it easier, it tries to parse the \*.bnk files into a \*.json equivalent, so you can dig through it by hand. There is a lot more room for this to improve, I just haven't put a lot of time into it.

As an example, here is how you would find the SoundFiles for an H5 Tag. You will need to use <http://wiki.xentax.com/index.php/Wwise_SoundBank_(*.bnk)> as a reference for more complicated Sound flows. This one will be easy, as it has only one piece of audio it points to.
```
Tag 001_vo_mul_mp_announcer_shared_killeryclinton_00100.sound =>
  Points to Event 0x561109E2 (E2-09-11-56 in Hex)

Within 0xCA956A62.json at english(us)\mpcu1\SoundBank (0xCA956A62)\
  Event 0x561109E2 points to EventAction 0x2CD6BB2D
  EventAction 0x2CD6BB2D points to SoundObject 0x366C9B00
  SoundObject 0x366C9B00 points to SoundFile 0x1AC6C5CB
```

So the SoundFile we want, is located at 'english(us)\mpcu1\SoundFiles\0x1AC6C5CB.ogg'

The SoundObject will be consistent between Languages, where the SoundFile that it points to will be unique. So, for example, if you find SoundObject 0x366C9B00 in 'spanish(spain)\mpcu1\SoundBank (0xCA956A62)\0xCA956A62.json', it will point to the translated SoundFile, which is 'spanish(spain)\mpcu1\SoundFiles\0x176ECEB6.ogg'
