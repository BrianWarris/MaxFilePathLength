using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace MaxFilePathLength
{
    class Program
    {
        static Random rand = new Random();
        // SEE https://stackoverflow.com/questions/1880321/why-does-the-260-character-path-length-limit-exist-in-windows
        // specification:  foreach drive found on the local workstation, start at 259 chars, and report length when and if maximum is exceeded.
        // IF response to 'reg   QUERY  HKLM\SYSTEM\CurrentControlSet\Control\FileSystem  /v "LongPathsEnabled"' is 0x1, report this.
        // do not text beyond a path length of 500; report maximum testing ceased at path length 500.
        /// <summary>
        /// Main = entry point
        /// </summary>
        /// <param name="args">string[]</param>
        /// <remarks>args are not used</remarks>
        static void Main(string[] args)
        {
            int maxDirectoryLength = 32;
            int maxFolderLength = 248;
            int initialFilePathLength = 259;
            string regKeyPath = "";
            string excludedTypes = "CDFS";
            bool supportsLongFilePaths = false;
            bool keepFileCreated = false;
            AppSettingsReader asr = new AppSettingsReader();
            GetConfigValue<int>(asr, "MaxFolderLength", ref maxFolderLength, 248);
            GetConfigValue<int>(asr, "MaxDirLength", ref maxDirectoryLength, 32);
            GetConfigValue<int>(asr, "InitialFilePathLength", ref initialFilePathLength, 260);
            GetConfigValue<string>(asr, "RegistryPath", ref regKeyPath, @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\FileSystem\LongPathsEnabled");
            GetConfigValue<string>(asr, "ExcludedTypes", ref excludedTypes, "CDFS");
            GetConfigValue<Boolean>(asr, "KeepFileCreated", ref keepFileCreated, false);
            ReportOnRegistrySetting(regKeyPath, out supportsLongFilePaths);
            System.Globalization.CultureInfo modCulture = new System.Globalization.CultureInfo("en-US");
            NumberFormatInfo number = modCulture.NumberFormat;
            number.NumberDecimalDigits = 0;
            // report which drives have been found, that are writable.
            DriveInfo[] di = DriveInfo.GetDrives();
            foreach (var die in di)
            {
                if (die.IsReady && !excludedTypes.Contains(die.DriveFormat))
                {
                    string readyState = (die.IsReady) ? "Ready" : "NotReady";
                    Console.Out.WriteLine($"See drive {die.Name,-7} {die.VolumeLabel,-10}  {die.DriveFormat,-5} {die.TotalSize.ToString("N", number),18} total; {die.TotalFreeSpace.ToString("N", number),15} free; {readyState,-8} {die.DriveType,-10}");
                    int maxLen = GetMaximumEmpiricalPathLength(die.Name, maxDirectoryLength, maxFolderLength, initialFilePathLength, supportsLongFilePaths, keepFileCreated);
                    Console.Out.WriteLine($"Maximum limit for a file path on this drive was determined to be {maxLen} characters.");
                }
            }
        }
        /// <summary>
        /// GetMaximumEmpiricalPathLength
        /// </summary>
        /// <param name="driveToTest">string of form such as C:\</param>
        /// <param name="maxDirectoryLength">int - string length of each subfolder</param>
        /// <param name="maxFolderLength">int - max string length of parent folder created</param>
        /// <param name="initialFilePathLength">int - initial test will have this file path length</param>
        /// <param name="supportsLongFilePaths">bool - evaluated and passed by the caller</param>
        /// <returns>int - the empirically evaluated maximum file path length</returns>
        static int GetMaximumEmpiricalPathLength(string driveToTest, int maxDirectoryLength, int maxFolderLength,
            int initialFilePathLength, bool supportsLongFilePaths, bool keepFileCreated)
        {
            int rv = 0;
            int initialLengthMax = maxFolderLength - 3;// see comments in the config file
            int currentLength = 0;
            int maximumLengthToTry = 500;
            string folderToRemove = "";
            StringBuilder sb = new StringBuilder(driveToTest);
            try
            {
                string sampleFolder = GetRandomString(maxDirectoryLength);
                sb.Append(sampleFolder);
                folderToRemove = sb.ToString();
                int diffInLength = initialLengthMax - sb.Length;
                while (diffInLength > maxDirectoryLength)
                {
                    sampleFolder = GetRandomString(maxDirectoryLength);
                    sb.Append($"{Path.DirectorySeparatorChar}{sampleFolder}");
                    diffInLength = initialLengthMax - sb.Length;
                }
                string rootFolder = sb.ToString();
                DirectoryInfo di = Directory.CreateDirectory(rootFolder);
                string FileToTry = GetRandomFileName(initialFilePathLength - sb.Length - 1);
                string fullFilePath = $"{rootFolder}{Path.DirectorySeparatorChar}{FileToTry}";
                bool haveReachedTheLimit = false;
                while (!haveReachedTheLimit)
                {
                    rv = fullFilePath.Length;
                    string comment = $"Attempting to create a file of {rv} characters long on {driveToTest}; ";
                    try
                    {
                        using (StreamWriter tw = new StreamWriter(fullFilePath))
                        {
                            tw.WriteLine(GetRandomString(80));
                            tw.Close();
                        }
                        Debug.WriteLine($"{comment}this succeeded.");
                        currentLength = fullFilePath.Length;
                    }
                    catch (Exception)
                    {
                        Debug.WriteLine($"{comment}this failed.");
                        if (!supportsLongFilePaths)
                        {
                            rv--;
                            haveReachedTheLimit = true;
                        }
                        else if (rv == maximumLengthToTry)
                        {
                            haveReachedTheLimit = true;
                        }
                    }
                    if (!haveReachedTheLimit)
                    {
                        if (supportsLongFilePaths)
                        {
                            // try the arbitrary limit:
                            FileToTry = GetRandomFileName(maximumLengthToTry - rootFolder.Length - 1);
                            fullFilePath = $"{rootFolder}{Path.DirectorySeparatorChar}{FileToTry}";
                        }
                        else
                        {
                            currentLength++;
                            FileToTry = GetRandomFileName(currentLength - rootFolder.Length - 1);
                            fullFilePath = $"{rootFolder}{Path.DirectorySeparatorChar}{FileToTry}";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.ToString());
            }
            finally
            {
                if (! keepFileCreated && ! string.IsNullOrEmpty(folderToRemove) && Directory.Exists(folderToRemove))
                {
                    Directory.Delete(folderToRemove, true);
                }
            }
            return rv;
        }
        /// <summary>
        /// GetRandomString - current design specified length and chooses upper case letters only.
        /// </summary>
        /// <param name="maxDirectoryLength"></param>
        /// <returns>random string of the length specified</returns>
        static string GetRandomString(int maxDirectoryLength)
        {
            // random mix of chars chosen from 0x41 to 0x5a (A..Z) up to maxDirectoryLength long
            List<char> chars = new List<char>(maxDirectoryLength);
            for (int i = 0; i < maxDirectoryLength; i++)
            {
                chars.Add((char)rand.Next(0x41, 0x5a));
            }
            return new string(chars.ToArray());
        }
        /// <summary>
        /// GetRandomFileName
        /// </summary>
        /// <param name="maxFileLen"></param>
        /// <returns>random file name of the length specifed</returns>
        static string GetRandomFileName(int maxFileLen)
        {
            return GetRandomString(maxFileLen - 4) + "." + GetRandomString(3);
        }
        /// <summary>
        /// ReportOnRegistrySetting - 
        /// </summary>
        /// <param name="regPath">string</param>
        /// <param name="supportsLongFilePaths">out bool</param>
        /// <see cref="https://stackoverflow.com/questions/1880321/why-does-the-260-character-path-length-limit-exist-in-windows"/>
        static void ReportOnRegistrySetting(string regPath, out bool supportsLongFilePaths)
        {
            supportsLongFilePaths = false;
            try
            {
                GetRegistryHive(regPath, out RegistryHive rh, out string SpecificFolder);
                if (!string.IsNullOrEmpty(SpecificFolder))
                {
                    string valueName = Path.GetFileName(SpecificFolder);
                    string specificKey = Path.GetDirectoryName(SpecificFolder);
                    Debug.WriteLine(rh.ToString());
                    RegistryKey rk = RegistryKey.OpenBaseKey(rh, RegistryView.Registry64);
                    if (rk != null)
                    {
                        RegistryKey sk = rk.OpenSubKey(specificKey);
                        if (sk != null)
                        {
                            object o = sk.GetValue(valueName);
//                            Debug.WriteLine($"type of key found at {regPath} is {o.GetType()}");
                            Console.Out.WriteLine($"Value of key found at {regPath} is {o}");
                            if (o is int)
                            {
                                Int32 value = (int)o;
                                string comment = (value == 0) ? "=> NO support for LongPaths exists on {0}" : "=> Support for LongPaths does exist on {0}";
                                Console.Out.WriteLine(comment, Environment.GetEnvironmentVariable("computername"));
                                supportsLongFilePaths = (value == 1);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"ReportOnRegistrySetting: encountered on the attempt to read {regPath}{Environment.NewLine}{ex.ToString()}");
            }
        }
        /// <summary>
        /// GetRegistryHive
        /// </summary>
        /// <param name="regPath">string</param>
        /// <param name="rh">out RegistryHive</param>
        /// <param name="specificFolder">out string</param>
        static void GetRegistryHive(string regPath, out RegistryHive rh, out string specificFolder)
        {
            rh = RegistryHive.LocalMachine;
            specificFolder = string.Empty;
            if (!string.IsNullOrEmpty(regPath) && regPath.Contains(@"\"))
            {
                int index = regPath.IndexOf('\\');
                string specificHive = regPath.Substring(0, index);
                specificFolder = regPath.Substring(index + 1);
                switch (specificHive.ToUpper())
                {
                    case "HKEY_LOCAL_MACHINE":
                    case "HKLM":
                    case "HKLM:":
                        rh = RegistryHive.LocalMachine;
                        break;
                    case "HKEY_CLASSES_ROOT":
                    case "HKCR":
                    case "HKCR:":
                        rh = RegistryHive.ClassesRoot;
                        break;
                    case "HKEY_CURRENT_CONFIG":
                    case "HKCC":
                    case "HKCC:":
                        rh = RegistryHive.CurrentConfig;
                        break;
                    case "HKEY_CURRENT_USER":
                    case "HKCU":
                    case "HKCU:":
                        rh = RegistryHive.CurrentUser;
                        break;
                    case "HKEY_DYN_DATA":
                    case "HKDD":
                    case "HKDD:":
                        rh = RegistryHive.DynData;
                        break;
                    case "HKEY_USERS":
                    case "HKU":
                    case "HKU:":
                        rh = RegistryHive.Users;
                        break;
                    default:
                        Console.Error.WriteLine($"{regPath} has no support. Change this to a supported registry hive!");
                        break;
                }
            }
        }
        
        /// <summary>
        /// GetConfigValue
        /// </summary>
        /// <typeparam name="T">passed type</typeparam>
        /// <param name="appSettingsReader">System.Configuration.AppSettingsReader</param>
        /// <param name="keyName">string</param>
        /// <param name="keyValue">ref T</param>
        /// <param name="defaultValue">T</param>
        private static void GetConfigValue<T>(System.Configuration.AppSettingsReader appSettingsReader,
                                        string keyName, ref T keyValue, T defaultValue)
        {
            keyValue = defaultValue; // provide a default
            try
            {
                string tempS = (string)appSettingsReader.GetValue(keyName, typeof(System.String));
                if ((tempS != null) && (tempS.Trim().Length > 0))
                {
                    keyValue = (T)TypeDescriptor.GetConverter(keyValue.GetType()).ConvertFrom(tempS);
                }
                else
                    Debug.WriteLine("Registry failed to read value from " + keyName);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());  // if key does not exist, not a problem. Caller must pre-assign values anyway
            }
        }
    }
}
