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


        public ReactiveCommand LocalPathPreviewDragOver { get; } = new ReactiveCommand();
        public AsyncReactiveCommand LocalPathDrop { get; } = new AsyncReactiveCommand();
        public ReactivePropertySlim<string> LocalPath { get; set; } = new ReactivePropertySlim<string>(string.Empty);
        public ReactivePropertySlim<string> RepoPath { get; set; } = new ReactivePropertySlim<string>(string.Empty);
        public ReactivePropertySlim<DirectoryItem> DirItems { get; set; }
        public ReactiveCommand<System.Windows.RoutedPropertyChangedEventArgs<object>> SelectItemChange { get; } = new ReactiveCommand<System.Windows.RoutedPropertyChangedEventArgs<object>>();
        public ReactivePropertySlim<string> SelectItem { get; set; } = new ReactivePropertySlim<string>(string.Empty);
        private DirectoryItem _selectedItem;
        public AsyncReactiveCommand UpdateOnlyItem { get; set; } = new AsyncReactiveCommand();
        // SVN Client
        private SharpSvn.SvnClient _client = new SharpSvn.SvnClient();
        // Log
        public ReactiveCollection<string> LogList { get; } = new ReactiveCollection<string>();

        public MainWindowViewModel()
        {
            _client
                .AddTo(disposables);
            // GUI
            LocalPathPreviewDragOver
                .Subscribe(e => OnPreviewDragOver_LocalPath((DragEventArgs)e))
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
            _selectedItem = DirItems.Value;
            SelectItemChange
                .WithSubscribe(x =>
                {
                    var item = (DirectoryItem)x.NewValue;
                    SelectItem.Value = item.Name;
                    _selectedItem = item;
                })
                .AddTo(disposables);
            SelectItem
                .AddTo(disposables);
            UpdateOnlyItem
                .WithSubscribe(async (x) =>
                {
                    if (_selectedItem is not null)
                    {
                        await _selectedItem.UpdateOnlyThisItem();
                    }
                })
                .AddTo(disposables);
        }

        private void OnPreviewDragOver_LocalPath(DragEventArgs e)
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
            try
            {
                var (root, uri) = await Task.Run(() =>
                {
                    var root = _client.GetWorkingCopyRoot(path);
                    var uri = _client.GetUriFromWorkingCopy(path);
                    return (root, uri);
                });
                // 既存情報チェック
                // 指定されたパスのrootがすでに取得済みの情報と同じならスキップ
                if (DirItems.Value.IsSameWorkCopy(path))
                {
                    await DirItems.Value.SelectItem(path);
                    return;
                }
                //
                if (uri is not null && root is not null)
                {
                    RepoPath.Value = uri.ToString();
                    var newlist = new DirectoryItem(_client);
                    await newlist.InitRoot(root, path);
                    DirItems.Value = newlist;
                }
                else
                {
                    RepoPath.Value = "<No SVN Versioned Item.>";
                }
            }
            catch
            {
                RepoPath.Value = "<Error Occur.>";
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
        public ReactivePropertySlim<BitmapSource> Icon { get; set; }
        public ReactivePropertySlim<bool> HasLocal { get; set; } = new ReactivePropertySlim<bool>(false);
        public ReactivePropertySlim<bool> HasRepo { get; set; } = new ReactivePropertySlim<bool>(false);
        //
        public ReactivePropertySlim<bool> IsExpanded { get; set; } = new ReactivePropertySlim<bool>(false);
        public ReactivePropertySlim<bool> IsSelected { get; set; } = new ReactivePropertySlim<bool>(false);
        //
        public ReactiveCollection<DirectoryItem> Items { get; set; } = new ReactiveCollection<DirectoryItem>();
        private Dictionary<string, DirectoryItem> _items = new Dictionary<string, DirectoryItem>();     // ノード検索用

        public DirectoryItem(SharpSvn.SvnClient client)
        {
            this.SvnClient = client;
            Icon = new ReactivePropertySlim<BitmapSource>(System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(SystemIcons.WinLogo.Handle, Int32Rect.Empty, null));
        }

        public async Task InitRoot(string root, string curr)
        {
            var rootRepo = await Task.Run(() =>
            {
                return SvnClient.GetUriFromWorkingCopy(root);
            });
            var rootItem = new DirectoryItem(SvnClient)
            {
                Parent = null,
                LocalPath = root,
                RepoPath = rootRepo,
            };
            await rootItem.Init(curr);
            // ツリー作成
            Items.Clear();
            Items.Add(rootItem);
        }

        public async Task Init(string select)
        {
            // 自分の情報を更新
            if (RepoPath is not null)
            {
                // リポジトリから情報取得
                var (result, isFile, isDirectory) = await Task.Run(() =>
                {
                    var tgt = SharpSvn.SvnTarget.FromUri(RepoPath);
                    if (SvnClient.GetInfo(tgt, out var info))
                    {
                        var isFile = (info.NodeKind == SharpSvn.SvnNodeKind.File);
                        var isDirectory = (info.NodeKind == SharpSvn.SvnNodeKind.Directory);
                        return (true, IsFile, isDirectory);
                    }
                    return (false, false, false);
                });
                if (result)
                {
                    IsFile = isFile;
                    IsDirectory = isDirectory;
                }
                HasRepo.Value = true;
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
                Icon.Value = GetIcon(LocalPath);
                HasLocal.Value = true;
            }
            // ディレクトリであれば配下情報を取得
            if (IsDirectory)
            {
                // 最初にリポジトリの該当フォルダ内のリストを取得
                var (dir_list, file_list) = await GetRepoList();
                // 
                string? name = null;
                if (LocalPath is not null)
                {
                    name = System.IO.Path.GetFileName(LocalPath);
                }
                else if (RepoPath is not null)
                {
                    //name = System.IO.Path.GetFileName(RepoPath.LocalPath);
                }
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
                        var repo = await Task.Run(() =>
                        {
                            return SvnClient.GetUriFromWorkingCopy(dir);
                        });
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
                            Name = dir_name,
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
                        var file_name = System.IO.Path.GetFileName(file);
                        var repo = await Task.Run(() =>
                        {
                            return SvnClient.GetUriFromWorkingCopy(file);
                        });
                        var item = new DirectoryItem(SvnClient)
                        {
                            Name = file_name,
                            Parent = this,
                            LocalPath = file,
                            RepoPath = repo,
                        };
                        Items.Add(item);
                        // 検索用辞書に登録
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
                            Name = file_name,
                            Parent = this,
                            RepoPath = file,
                        };
                        Items.Add(item);
                    }
                }
                // 最後にまとめて初期化を実施
                foreach (var item in Items)
                {
                    await item.Init(select);
                }
            }
            if (IsFile)
            {
                string? name = null;
                if (LocalPath is not null)
                {
                    name = System.IO.Path.GetFileName(LocalPath);
                }
                else if (RepoPath is not null)
                {
                    //name = System.IO.Path.GetFileName(RepoPath.LocalPath);
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

        public async Task SelectItem(string path)
        {
            // root要素に対して実行することを前提とする
            if (Parent is not null) return;
            // pathが自分より下の階層にいることを前提に探索する
            try
            {
                // パスをファイル、ディレクトリごとで分割
                var parts = await Task.Run(() =>
                {
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
                    return parts;
                });
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

        public async Task<bool> UpdateOnlyThisItem()
        {
            try
            {
                // 選択しているアイテムだけUPDATEで取得する
                // ローカルにWorkingCopyが無く、リポジトリに存在する前提
                if (LocalPath is not null || RepoPath is null) return false;
                var (result, update_list) = await Task.Run(() =>
                {
                    // WorkingCopyが存在する場所までさかのぼって、順に1つずつ取得する
                    var update_list = new LinkedList<DirectoryItem>();
                    var curr = this;
                    var result = true;
                    while (curr.LocalPath is null)
                    {
                        // LocalPathが無ければUPDATE対象
                        update_list.AddFirst(curr);
                        // WorkingCopyのrootが必ず存在するのでnull到達はありえない
                        if (curr.Parent is null)
                        {
                            result = false;
                            break;
                        }
                        curr = curr.Parent;
                    }
                    return (result, update_list);
                });
                if (!result) return false;
                // 最上位フォルダから順にUPDATE
                var args = new SharpSvn.SvnUpdateArgs();
                args.Depth = SharpSvn.SvnDepth.Children;
                args.KeepDepth = true;
                args.Revision = SharpSvn.SvnRevision.Head;
                foreach (var item in update_list)
                {
                    var update_result = await Task.Run(() =>
                    {
                        var res = SvnClient.Update(item.Parent!.LocalPath, args, out var result);
                        return (res, result);
                    });
                    if (update_result.res)
                    {
                        var name = item.Name;
                        if (item.IsDirectory) name = item.Name.Substring(0, item.Name.Length - 1);
                        item.LocalPath = item.Parent!.LocalPath + System.IO.Path.DirectorySeparatorChar + name;
                        item.Icon.Value = GetIcon(item.LocalPath);
                        item.HasLocal.Value = true;
                    }
                    else
                    {
                        return false;
                    }
                }
            }
            catch
            {
                return false;
            }

            return true;
        }

        private async Task<(LinkedList<Uri>, LinkedList<Uri>)> GetRepoList()
        {
            return await Task.Run(() =>
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
            });
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
