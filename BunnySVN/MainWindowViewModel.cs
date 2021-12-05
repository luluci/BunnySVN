using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reactive.Disposables;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Drawing;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;
using System.Windows.Media.Imaging;

namespace BunnySVN
{
    internal class MainWindowViewModel : IDisposable, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private bool disposedValue;
        private CompositeDisposable disposables = new CompositeDisposable();


        public AsyncReactiveCommand LocalPathPreviewDragOver { get; } = new AsyncReactiveCommand();
        public AsyncReactiveCommand LocalPathDrop { get; } = new AsyncReactiveCommand();
        public ReactivePropertySlim<string> LocalPath { get; set; } = new ReactivePropertySlim<string>(string.Empty);
        public ReactivePropertySlim<string> RepoPath { get; set; } = new ReactivePropertySlim<string>(string.Empty);
        public ReactivePropertySlim<DirectoryItem> DirItems { get; set; }
        public AsyncReactiveCommand<System.Windows.RoutedPropertyChangedEventArgs<object>> SelectItemChange { get; } = new AsyncReactiveCommand<System.Windows.RoutedPropertyChangedEventArgs<object>>();
        public ReactivePropertySlim<string> SelectItem { get; set; } = new ReactivePropertySlim<string>(string.Empty);
        // SVN Client
        private SharpSvn.SvnClient _client = new SharpSvn.SvnClient();

        public MainWindowViewModel()
        {
            // GUI
            LocalPathPreviewDragOver
                .Subscribe(async (e) => await OnPreviewDragOver_LocalPath((DragEventArgs)e))
                .AddTo(disposables);
            LocalPathDrop
                .Subscribe(async (e) => await OnFileDrop_LocalPath((DragEventArgs)e))
                .AddTo(disposables);
            LocalPath
                .AddTo(disposables);
            RepoPath
                .AddTo(disposables);
            // ディレクトリ
            DirItems = new ReactivePropertySlim<DirectoryItem>(new DirectoryItem(_client));
            DirItems
                .AddTo(disposables);
            SelectItemChange
                .WithSubscribe(async (x) =>
                {
                    var item = (DirectoryItem)x.NewValue;
                    SelectItem.Value = item.Name;
                })
                .AddTo(disposables);
            SelectItem
                .AddTo(disposables);
        }

        private async Task OnPreviewDragOver_LocalPath(DragEventArgs e)
        {
            // マウスポインタを変更する。
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.All;
                e.Handled = true;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
        }

        private async Task OnFileDrop_LocalPath(DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                // ドロップしたファイル名を全部取得する。
                string[] filenames = (string[])e.Data.GetData(DataFormats.FileDrop);
                LocalPath.Value = filenames[0];

                await CheckLocalPath(LocalPath.Value);
            }
        }

        private async Task CheckLocalPath(string path)
        {
            var root = _client.GetWorkingCopyRoot(path);
            var uri = _client.GetUriFromWorkingCopy(path);
            // 既存情報チェック
            // 指定されたパスのrootがすでに取得済みの情報と同じならスキップ
            if (DirItems.Value.IsSameWorkCopy(path))
            {
                DirItems.Value.SelectItem(path);
                return;
            }
            //
            if (uri is not null && root is not null)
            {
                var newlist = new DirectoryItem(_client);
                newlist.InitRoot(root, path);
                RepoPath.Value = uri.ToString();
                DirItems.Value = newlist;
            }
            else
            {
                RepoPath.Value = "<No SVN Versioned Item.>";
            }
        }

        #region Dispose
        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // TODO: マネージド状態を破棄します (マネージド オブジェクト)
                    disposables.Dispose();
                }

                // TODO: アンマネージド リソース (アンマネージド オブジェクト) を解放し、ファイナライザーをオーバーライドします
                // TODO: 大きなフィールドを null に設定します
                disposedValue = true;
            }
        }

        // // TODO: 'Dispose(bool disposing)' にアンマネージド リソースを解放するコードが含まれる場合にのみ、ファイナライザーをオーバーライドします
        // ~MainWindowViewModel()
        // {
        //     // このコードを変更しないでください。クリーンアップ コードを 'Dispose(bool disposing)' メソッドに記述します
        //     Dispose(disposing: false);
        // }

        public void Dispose()
        {
            // このコードを変更しないでください。クリーンアップ コードを 'Dispose(bool disposing)' メソッドに記述します
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
        #endregion 
    }

    public class DirectoryItem : INotifyPropertyChanged
    {

        public event PropertyChangedEventHandler? PropertyChanged;

        //
        public DirectoryItem? Parent { get; set; } = null;
        // Local/Repo両方ともPathがnullのものをroot itemとする
        public SharpSvn.SvnClient SvnClient { get; set; }
        public string? LocalPath { get; set; } = null;
        public Uri? RepoPath { get; set; } = null;
        //
        public string Name { get; set; } = string.Empty;
        public bool IsFile { get; set; } = false;
        public bool IsDirectory { get; set; } = false;
        public BitmapSource Icon { get; set; }
        public bool HasLocal { get; set; } = false;
        public bool HasRepo { get; set; } = false;
        //
        public ReactivePropertySlim<bool> IsExpanded { get; set; } = new ReactivePropertySlim<bool>(false);
        public ReactivePropertySlim<bool> IsSelected { get; set; } = new ReactivePropertySlim<bool>(false);
        //
        public ObservableCollection<DirectoryItem> Items { get; set; } = new ObservableCollection<DirectoryItem>();
        private Dictionary<string, DirectoryItem> _items = new Dictionary<string, DirectoryItem>();     // ノード検索用

        public DirectoryItem(SharpSvn.SvnClient client)
        {
            this.SvnClient = client;
            Icon = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(SystemIcons.WinLogo.Handle, Int32Rect.Empty, null);
        }

        public void InitRoot(string root, string curr)
        {
            var rootRepo = SvnClient.GetUriFromWorkingCopy(root);
            var rootItem = new DirectoryItem(SvnClient)
            {
                Parent = null,
                LocalPath = root,
                RepoPath = rootRepo,
            };
            rootItem.Init(curr);
            // ツリー作成
            Items.Clear();
            Items.Add(rootItem);
        }

        public void Init(string select)
        {
            // 自分の情報を更新
            if (RepoPath is not null)
            {
                // リポジトリから情報取得
                var tgt = SharpSvn.SvnTarget.FromUri(RepoPath);
                if (SvnClient.GetInfo(tgt, out var info))
                {
                    IsFile = (info.NodeKind == SharpSvn.SvnNodeKind.File);
                    IsDirectory = (info.NodeKind == SharpSvn.SvnNodeKind.Directory);
                }
                HasRepo = true;
            }
            if (LocalPath is not null)
            {
                // 初期選択アイテムチェック
                if (LocalPath == select)
                {
                    IsSelected.Value = true;
                    IsExpanded.Value = true;
                    // 展開して見せる
                    ExpandRoot();
                }
                // ローカルファイル情報取得
                IsFile = System.IO.File.Exists(LocalPath);
                IsDirectory = System.IO.Directory.Exists(LocalPath);
                Icon = GetIcon(LocalPath);
                HasLocal = true;
            }
            // ディレクトリであれば配下情報を取得
            if (IsDirectory)
            {
                // 最初にリポジトリの該当フォルダ内のリストを取得
                var (dir_list, file_list) = GetRepoList();
                // 
                var name = System.IO.Path.GetFileName(LocalPath);
                if (name is not null)
                {
                    Name = name + "/";
                }
                // ディレクトリをすべて登録
                if (LocalPath is not null)
                {
                    foreach (var dir in System.IO.Directory.EnumerateDirectories(LocalPath))
                    {
                        if (dir is null) continue;
                        var dir_name = System.IO.Path.GetFileName(dir);
                        if (dir_name is null) continue;
                        dir_name += "/";
                        if (dir_name == ".svn/") continue;
                        var repo = SvnClient.GetUriFromWorkingCopy(dir);
                        var item = new DirectoryItem(SvnClient)
                        {
                            Parent = this,
                            LocalPath = dir,
                            RepoPath = repo,
                        };
                        Items.Add(item);
                        // 検索用辞書に登録
                        if (!_items.ContainsKey(dir_name))
                        {
                            _items.Add(dir_name, item);
                        }
                    }
                }
                // リポジトリにのみ存在するフォルダを登録
                foreach (var dir in dir_list)
                {
                    var dir_name = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(dir.LocalPath));
                    if (dir_name is null) dir_name = string.Empty;
                    dir_name += "/";
                    if (!_items.ContainsKey(dir_name))
                    {
                        var item = new DirectoryItem(SvnClient)
                        {
                            Parent = this,
                            RepoPath = dir,
                        };
                        Items.Add(item);
                    }
                }
                // ファイルをすべて登録
                if (LocalPath is not null)
                {
                    foreach (var file in System.IO.Directory.EnumerateFiles(LocalPath))
                    {
                        var repo = SvnClient.GetUriFromWorkingCopy(file);
                        var item = new DirectoryItem(SvnClient)
                        {
                            Parent = this,
                            LocalPath = file,
                            RepoPath = repo,
                        };
                        Items.Add(item);
                        // 検索用辞書に登録
                        var file_name = System.IO.Path.GetFileName(file);
                        if (file_name is null) file_name = string.Empty;
                        if (!_items.ContainsKey(file_name))
                        {
                            _items.Add(file_name, item);
                        }
                    }
                }
                // リポジトリにのみ存在するファイルを登録
                foreach (var file in file_list)
                {
                    var file_name = System.IO.Path.GetFileName(file.LocalPath);
                    if (file_name is null) file_name = string.Empty;
                    if (!_items.ContainsKey(file_name))
                    {
                        var item = new DirectoryItem(SvnClient)
                        {
                            Parent = this,
                            RepoPath = file,
                        };
                        Items.Add(item);
                    }
                }
                // 最後にまとめて初期化を実施
                foreach (var item in Items)
                {
                    item.Init(select);
                }
            }
            if (IsFile)
            {
                string? name = null;
                if (LocalPath is not null)
                {
                    name = System.IO.Path.GetFileName(LocalPath);
                }
                if (RepoPath is not null)
                {
                    name = System.IO.Path.GetFileName(RepoPath.LocalPath);
                }
                if (name is not null)
                {
                    Name = name;
                }
            }
        }

        private void ExpandRoot()
        {
            var parent = Parent;
            while (parent is not null)
            {
                parent.IsExpanded.Value = true;
                parent = parent.Parent;
            }
        }

        public bool IsSameWorkCopy(string path)
        {
            bool result = false;

            try
            {
                // 指定されたパスのrootが自分と同じWorkingCopy配下にいるかチェック
                var root = SvnClient.GetWorkingCopyRoot(path);
                if (Items.Count > 0)
                {
                    var rootPath = Items[0].LocalPath;
                    if (rootPath is not null && rootPath == root)
                    {
                        result = true;
                    }
                }
            }
            catch
            {
                result = false;
            }

            return result;
        }

        public void SelectItem(string path)
        {
            // root要素に対して実行することを前提とする
            if (Parent is not null) return;
            // pathが自分より下の階層にいることを前提に探索する
            try
            {
                // パスをファイル、ディレクトリごとで分割
                var root = SvnClient.GetWorkingCopyRoot(path);
                var parts = new LinkedList<string>();
                string? next = path;
                string? part = null;
                while (root != next)
                {
                    if (System.IO.File.Exists(next))
                    {
                        part = System.IO.Path.GetFileName(next);
                    }
                    else if (System.IO.Directory.Exists(next))
                    {
                        part = System.IO.Path.GetFileName(next) + "/";
                    }
                    else
                    {
                        break;
                    }
                    next = System.IO.Path.GetDirectoryName(next);
                    if (next is null) break;
                    parts.AddFirst(part);
                }
                // 探索
                var curr = Items[0];
                foreach (var p in parts)
                {
                    foreach (var child in curr.Items)
                    {
                        if (child.Name == p)
                        {
                            curr.IsExpanded.Value = true;
                            curr = child;
                            break;
                        }
                    }
                }
                if (curr is not null)
                {
                    curr.IsExpanded.Value = true;
                    curr.IsSelected.Value = true;
                }
            }
            catch
            {
                // 
            }
        }

        private (LinkedList<Uri>, LinkedList<Uri>) GetRepoList()
        {
            var dir_list = new LinkedList<Uri>();
            var file_list = new LinkedList<Uri>();
            if (RepoPath is not null)
            {
                // ライブラリの挙動がおかしい？　RepositoryRootがSvnTargetの親ディレクトリになってるっぽい？
                // RepositoryRootにBasePath(rootからの相対パスっぽい)を結合するのでパスがおかしくなってる
                // そのため、RepoPathにPathを結合して正しいパスを作成する
                var tgt = SharpSvn.SvnTarget.FromUri(RepoPath);
                if (SvnClient.GetList(tgt, out var list))
                {
                    foreach (var item in list)
                    {
                        var path = item.Path;
                        // 空文字は自分を指してるのでスキップ
                        if (path.Length == 0) continue;
                        // その他の子アイテムを処理
                        switch (item.Entry.NodeKind)
                        {
                            case SharpSvn.SvnNodeKind.Directory:
                                //dir_list.AddLast(item.Uri);
                                var new_path = new Uri(RepoPath, path + "/");
                                dir_list.AddLast(new_path);
                                break;
                            case SharpSvn.SvnNodeKind.File:
                                //file_list.AddLast(item.Uri);
                                var new_file_path = new Uri(RepoPath, path);
                                file_list.AddLast(new_file_path);
                                break;
                            default:
                                // 何もしない
                                break;
                        }
                    }
                }
            }
            return (dir_list, file_list);
        }

        private BitmapSource GetIcon(string path)
        {
            WindowsShellAPI.SHFILEINFO shinfo = new();
            IntPtr hImg = WindowsShellAPI.SHGetFileInfo(
              path, 0, out shinfo, (uint)Marshal.SizeOf(typeof(WindowsShellAPI.SHFILEINFO)),
              WindowsShellAPI.SHGFI.SHGFI_ICON | WindowsShellAPI.SHGFI.SHGFI_LARGEICON);

            return System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(shinfo.hIcon, Int32Rect.Empty, null);

            //return Icon.FromHandle(shinfo.hIcon);
        }
    }
}
