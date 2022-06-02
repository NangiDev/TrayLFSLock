
using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System.Linq;
using System.Diagnostics;
using LibGit2Sharp;
using System.Collections.Generic;
using System.Configuration;

namespace TrayLock
{
    public partial class LockTray : Form
    {
        private static readonly string repoPath = ConfigurationManager.AppSettings.Get("RepoPath") ?? "";
        public static string GetRepoPath()
        {
            return repoPath;
        }

        private static readonly string repoName = ConfigurationManager.AppSettings.Get("RepoName") ?? "";
        public static string GetRepoName()
        {
            return repoName;
        }

        private NotifyIcon sysTrayIcon;
        private ContextMenuStrip sysTrayMenu;

        private static readonly Repository repo = new(GetRepoPath());
        private static readonly HashSet<string> LockedFilePaths = new();
        private static string[] Extensions = Array.Empty<string>();

        public LockTray()
        {
            InitializeComponent();
            sysTrayMenu = InitTrayMenu();
            sysTrayIcon = InitIcon();
            ListDirectory(FileTreeView, GetRepoPath());
            return;
        }
        private static void ListDirectory(TreeView treeView, string path)
        {
            var gitattributes = (path + "/.gitattributes").Replace("\\","/");
            Extensions = File.ReadAllLines(gitattributes)
                .Where(x =>
                    !x.StartsWith("#") &&
                    x.ToLower().Contains("filter=lfs") &&
                    x.ToLower().Contains("*."))
                .Select(x => { return x[1..x.IndexOf(" ")]; })
                .ToArray();

            var info = new ProcessStartInfo("git", $"lfs locks")
            {
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                WorkingDirectory = repo.Info.Path,
            };

            var lockProcess = Process.Start(info);
            lockProcess?.WaitForExit();
            if (lockProcess?.ExitCode != 0) return;

            using (var sr = lockProcess?.StandardOutput) {

                string? line;
                while ((line = sr?.ReadLine()) != null)
                {
                    line = "." + line.Split("\t").First().Replace("./", "/").Replace("'","").Replace(" ","");
                    LockedFilePaths.Add(line);
                }
            }

            var rootNode = new TreeNode(GetRepoName());
            rootNode.BackColor = Color.LightGray;
            Dictionary<string, TreeNode> structureTree = new()
            {
                { GetRepoName(), rootNode }
            };

            foreach (IndexEntry e in repo.Index)
            {
                var extension = e.Path[e.Path.LastIndexOf(".")..];
                if (e.Path.Contains("RawContent") || (Extensions.Contains(extension) && !repo.Ignore.IsPathIgnored(e.Path)))
                    ProcessPathLine(e.Path, structureTree);
            }

            treeView.Nodes.Clear();
            treeView.ShowLines = true;
            treeView.FullRowSelect = true;
            treeView.ShowPlusMinus = true;
            treeView.Indent = 12;

            treeView.Nodes.Add(rootNode);
        }
        private static void ProcessPathLine(string? line, Dictionary<string, TreeNode> structureTree)
        {
            if (line == null) return;

            line = ("/" + line).Replace("/", "\\/");
            var delimiter = "\\";
            var parents = line.Split(delimiter);

            string key = ".";
            for (var i = 1; i < parents.Length; i++)
            {
                var path = key + parents[i];
                var name = parents[i][1..];
                var node = new TreeNode(name);

                if(LockedFilePaths.Contains(path))
                {
                    node.BackColor = Color.Red;
                    node.ForeColor = Color.White;
                } else
                {
                    node.BackColor = Color.White;
                    node.ForeColor = Color.Black;
                }

                if (structureTree.TryGetValue(key, out var parentNode))
                {
                    parentNode.BackColor = Color.LightGray;
                    if(structureTree.TryAdd(path, node))
                        parentNode.Nodes.Add(node);
                } else
                {
                    if(structureTree.TryAdd(path, node))
                        structureTree[GetRepoName()].Nodes.Add(node);
                }
                key += parents[i];
            }
        }

        private ContextMenuStrip InitTrayMenu()
        {
            sysTrayMenu = new();
            sysTrayMenu.Items.Add(new ToolStripMenuItem("Exit", null, OnExit, "Exit"));
            return sysTrayMenu;
        }

        private NotifyIcon InitIcon()
        {
            sysTrayIcon = new()
            {
                Text = "TrayLock",
                Icon = new Icon(SystemIcons.Shield, 40, 40),
                ContextMenuStrip = sysTrayMenu,
                Visible = true
            };
            sysTrayIcon.MouseClick += LeftClickShowMenu;
            return sysTrayIcon;
        }

        protected override void OnLoad(EventArgs e)
        {
            //Show();
            Hide();
            ShowInTaskbar = false;
            base.OnLoad(e);
            FileTreeView.ExpandAll();
        }
        private void LeftClickShowMenu(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                TopMost = true;
                var _screen = Screen.FromControl(this).Bounds;
                var _point = new Point(Cursor.Position.X, Cursor.Position.Y);
                Top = _point.Y - Height - 10;
                Left = _screen.Width - Width - 20;

                if (Visible)
                    Hide();
                else
                    Show();
            }
        }
        private void OnExit(object? sender, EventArgs e)
        {
            Application.Exit();
        }

        private void LockBtn_Click(object sender, EventArgs e)
        {
            var fileName = FileTreeView.SelectedNode.FullPath.Replace("\\", "/").Replace(GetRepoName(), ".");
            if (LockedFilePaths.Contains(fileName)) return;

            var info = new ProcessStartInfo("git", $"lfs lock '{fileName}'")
            {
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                WorkingDirectory = GetRepoPath(),
            };

            var lockProcess = Process.Start(info);
            Enabled = false;
            lockProcess?.WaitForExit();
            Enabled = true;

            if (lockProcess?.ExitCode != 0) return;
            LockedFilePaths.Add(fileName);
            FileTreeView.SelectedNode.BackColor = Color.Red;
            FileTreeView.SelectedNode.ForeColor = Color.White;
            FileTreeView.SelectedNode = null;
            FileTreeView.Focus();
        }

        private void UnlockBtn_Click(object sender, EventArgs e)
        {
            var fileName = FileTreeView.SelectedNode.FullPath.Replace("\\", "/").Replace(GetRepoName(), ".");
            if (!LockedFilePaths.Contains(fileName)) return;

            var info = new ProcessStartInfo("git", $"lfs unlock '{fileName}'")
            {
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                WorkingDirectory = GetRepoPath(),
            };

            var lockProcess = Process.Start(info);
            Enabled = false;
            lockProcess?.WaitForExit();
            Enabled = true;

            if (lockProcess?.ExitCode != 0) return;
            LockedFilePaths.Remove(fileName);
            FileTreeView.SelectedNode.BackColor = Color.White;
            FileTreeView.SelectedNode.ForeColor = Color.Black;
            FileTreeView.SelectedNode = null;
            FileTreeView.Focus();
        }

        private void FileTreeView_BeforeSelect(object sender, TreeViewCancelEventArgs e)
        {
            if (e.Node.Nodes.Count > 0)
                e.Cancel = true;
        }
    }
}