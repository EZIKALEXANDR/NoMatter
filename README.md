# NoMatter
<img width="500" height="500" alt="image" src="https://github.com/user-attachments/assets/6459d42c-1a0e-4c7f-b1c7-8ebda243995c" />

NoMatter is a destructive trojan written in C#. It is designed to delete user data at extremely high speed, without giving the possibility of recovering this data.

# Execute
The virus registers itself as a critical system process, exactly until the moment when the encryption is completed. It executes hidden commands that completely wipe out Windows Shadow Copies (vssadmin), delete backup catalogs (wbadmin), and disable System Restore points across all active drives, also it alters the Windows boot configuration (bcdedit) to disable automatic recovery and safe mode troubleshooting options. It generates a 256-bit AES key in the computer's memory, uses it to overwrite files(but it skips Windows folder), and then completely destroys the key, making data recovery impossible once the application closes. At the end, an msgbox will be shown with a small amount of information and congratulations.

# Decryption?
Probably its possible. But you should create a virus process dump file(via taskmgr for example) before you close it. If you closed the process without doing so, restoring the data is pointless. You can try to get an encryption key via findaes, like type findaes.exe NoMatter.DMP and you will get something like 
```
Found AES-256 key schedule at offset 0x2d561c:
f9 05 7e 51 82 63 46 e5 3a ab c7 3a 99 0b 96 c6 b3 aa 48 08 5f 27 bd c7 84 4e c9 13 db ce c9 f2
Found AES-256 key schedule at offset 0x2d601c:
f9 05 7e 51 82 63 46 e5 3a ab c7 3a 99 0b 96 c6 b3 aa 48 08 5f 27 bd c7 84 4e c9 13 db ce c9 f2 
```

And with this data, you can write a decryptor, but actually I didnt try

# Compile
Run compile.bat

## Disclaimer

**All information and software provided in this repository are for educational and research purposes only.** 

The author is not responsible for any misuse, damage, or illegal activity caused by this software. Use it entirely at your own risk. The software is provided "as is", without warranty of any kind, express or implied. 

By downloading or using this software, you agree that you are solely responsible for your actions and any consequences that may arise.
