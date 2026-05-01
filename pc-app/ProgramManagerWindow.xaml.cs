using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;

namespace NexusRemotePC;

public partial class ProgramManagerWindow : Window
{
    private readonly CompanionStore _store;
    private readonly ObservableCollection<ProgramEntry> _programs;

    public ProgramManagerWindow(CompanionStore store)
    {
        InitializeComponent();
        _store = store;
        _programs = new ObservableCollection<ProgramEntry>(_store.LoadPrograms());
        ProgramsList.ItemsSource = _programs;
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Выбери программу",
            Filter = "Программы и ярлыки|*.exe;*.lnk|Все файлы|*.*",
            Multiselect = true
        };

        if (dialog.ShowDialog(this) != true) return;

        foreach (var path in dialog.FileNames)
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(path);
            if (_programs.Any(program => string.Equals(program.Path, path, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }
            _programs.Add(new ProgramEntry(name, path));
        }
        Save();
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        var selected = ProgramsList.SelectedItems.Cast<ProgramEntry>().ToArray();
        foreach (var item in selected)
        {
            _programs.Remove(item);
        }
        Save();
    }

    private void Run_Click(object sender, RoutedEventArgs e)
    {
        if (ProgramsList.SelectedItem is not ProgramEntry entry) return;
        try
        {
            Process.Start(new ProcessStartInfo(entry.Path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, ex.Message, "Не удалось запустить", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Save();
        System.Windows.MessageBox.Show(this, "Список сохранён.", "Nexus Remote PC", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Save() => _store.SavePrograms(_programs);
}
