﻿namespace Hasher
{
	#region Using Directives

	using System;
	using System.Collections.Generic;
	using System.Diagnostics.CodeAnalysis;
	using System.IO;
	using System.Linq;
	using System.Reflection;
	using System.Runtime.Remoting;
	using System.Runtime.Remoting.Metadata.W3cXsd2001;
	using System.Security.Cryptography;
	using System.Text;
	using System.Threading.Tasks;
	using System.Windows;
	using System.Windows.Controls;
	using System.Windows.Data;
	using System.Windows.Documents;
	using System.Windows.Input;
	using System.Windows.Media;
	using System.Windows.Media.Imaging;
	using System.Windows.Navigation;
	using System.Windows.Shapes;
	using System.Windows.Threading;
	using Menees;
	using Menees.Windows.Presentation;
	using Microsoft.Win32;

	#endregion

	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow
	{
		#region Private Data Members

		private WindowSaver saver;
		private Stream threadSafeStream;

		#endregion

		#region Constructors

		public MainWindow()
		{
			this.InitializeComponent();

			this.saver = new WindowSaver(this);
			this.saver.LoadSettings += this.Saver_LoadSettings;
			this.saver.SaveSettings += this.Saver_SaveSettings;
		}

		#endregion

		#region Private Methods

		private static void EnableControls(IEnumerable<Control> controls, bool isEnabled)
		{
			foreach (Control control in controls)
			{
				control.IsEnabled = isEnabled;
			}
		}

		private static void AddErrors(ICollection<Block> blocks, params string[] messages)
		{
			AddMessages(blocks, Brushes.Red, messages);
		}

		private static void AddImageMessage(ICollection<Block> blocks, string imageResourceName, Brush foreground, string message)
		{
			Image image = new Image();
			image.Stretch = Stretch.None;
			string imageUri = $"pack://application:,,,/{typeof(MainWindow).Assembly.FullName};component/Resources/{imageResourceName}24.png";
			image.Source = new BitmapImage(new Uri(imageUri, UriKind.Absolute));
			Paragraph paragraph = new Paragraph();
			paragraph.Inlines.Add(new InlineUIContainer(image));
			Run messageRun = new Run(' ' + message);
			messageRun.Foreground = foreground;
			messageRun.BaselineAlignment = BaselineAlignment.Center;
			paragraph.Inlines.Add(messageRun);
			blocks.Add(paragraph);
		}

		private static void AddMessages(ICollection<Block> blocks, params string[] messages)
		{
			AddMessages(blocks, null, messages);
		}

		private static void AddMessages(ICollection<Block> blocks, Brush foreground, params string[] messages)
		{
			Paragraph paragraph = new Paragraph();
			if (foreground != null)
			{
				paragraph.Foreground = Brushes.Red;
			}

			foreach (string message in messages)
			{
				if (paragraph.Inlines.Count > 0)
				{
					paragraph.Inlines.Add(new LineBreak());
				}

				paragraph.Inlines.Add(new Run(message));
			}

			blocks.Add(paragraph);
		}

		[SuppressMessage(
			"Microsoft.Design",
			"CA1031:DoNotCatchGeneralExceptionTypes",
			Justification = "HashAlgorithm.Create doesn't document its exception types, and this is in a top-level validation method.")]
		private List<Block> Validate(out string fileName, out HashAlgorithm hasher, out byte[] compareHash)
		{
			List<Block> result = new List<Block>();

			fileName = this.fileEdit.Text.Trim();
			if (!File.Exists(fileName))
			{
				AddErrors(result, "The specified file does not exist.");
			}

			hasher = null;
			ComboBoxItem item = this.algorithm.SelectedItem as ComboBoxItem;
			if (item == null)
			{
				AddErrors(result, "An algorithm must be selected.");
			}
			else
			{
				try
				{
					hasher = HashAlgorithm.Create((string)item.Tag);
				}
				catch (Exception ex)
				{
					AddErrors(result, "Unable to use the selected algorithm: " + ex.Message);
				}
			}

			compareHash = null;
			if (!string.IsNullOrEmpty(this.compareTo.Text))
			{
				try
				{
					SoapHexBinary binary = SoapHexBinary.Parse(this.compareTo.Text);
					compareHash = binary.Value;
					const int BitsPerByte = 8;
					int compareHashSize = BitsPerByte * compareHash.Length;
					if (hasher != null && hasher.HashSize != compareHashSize)
					{
						AddErrors(
							result,
							"The Compare To hash value is not the required length for the selected algorithm.",
							$"Compare To length: {compareHashSize} bits",
							$"Required length: {hasher.HashSize} bits");
					}
				}
				catch (RemotingException)
				{
					AddErrors(result, "Unable to parse the Compare To hash value as a sequence of hexadecimal digit pairs.");
				}
			}

			return result;
		}

		private async Task StartAsync()
		{
			FlowDocument document = new FlowDocument();
			this.status.Document = document;
			string fileName;
			HashAlgorithm hasher;
			byte[] compareHash;
			List<Block> messages = this.Validate(out fileName, out hasher, out compareHash);
			if (messages.Count > 0)
			{
				document.Blocks.AddRange(messages);
			}
			else
			{
				Control[] controls = new Control[] { this.fileEdit, this.selectFileButton, this.algorithm, this.compareTo };
				object originalButtonContent = this.startButton.Content;
				DispatcherTimer timer = new DispatcherTimer();
				try
				{
					using (Stream stream = Stream.Synchronized(File.OpenRead(fileName)))
					{
						this.threadSafeStream = stream;
						this.startButton.Content = "Ca_ncel";
						this.startButton.IsCancel = true;
						long streamLength = stream.Length;
						timer.Tick += (s, e) => this.progress.Value = streamLength != 0 ? 100 * (stream.Position / (double)streamLength) : 0;
						timer.Interval = TimeSpan.FromSeconds(1);
						timer.IsEnabled = true;
						EnableControls(controls, false);

						byte[] hash = await Task.Run(() => hasher.ComputeHash(stream));
						if (this.threadSafeStream != null)
						{
							SoapHexBinary binary = new SoapHexBinary(hash);
							AddMessages(messages, binary.ToString());

							if (compareHash != null)
							{
								if (hash.SequenceEqual(compareHash))
								{
									AddImageMessage(messages, "Ok", Brushes.Green, "EQUAL: The computed hash and the Compare To values match.");
								}
								else
								{
									AddImageMessage(messages, "Error", Brushes.Red, "NOT EQUAL: The computed hash and the Compare To values do NOT match.");
								}
							}
						}
						else
						{
							AddImageMessage(messages, "Warning", Brushes.DarkGoldenrod, "Canceled.");
						}
					}
				}
				catch (Exception ex)
				{
					AddErrors(messages, ex.Message);
				}
				finally
				{
					timer.IsEnabled = false;
					this.threadSafeStream = null;
					this.progress.Value = 0;
					this.startButton.Content = originalButtonContent;
					this.startButton.IsCancel = false;
					EnableControls(controls, true);
					document.Blocks.AddRange(messages);
				}
			}
		}

		private void Cancel()
		{
			if (this.threadSafeStream != null)
			{
				// HashAlgorithm.ComputeHash doesn't support cancelling, so all we can do
				// is force it to the end of the stream to make it finish faster.
				this.threadSafeStream.Seek(0, SeekOrigin.End);
				this.threadSafeStream = null;
			}
		}

		#endregion

		#region Private Event Handlers

		private void About_Click(object sender, RoutedEventArgs e)
		{
			WindowsUtility.ShowAboutBox(this, Assembly.GetExecutingAssembly());
		}

		private void Saver_LoadSettings(object sender, SettingsEventArgs e)
		{
			var settings = e.SettingsNode;
			this.fileEdit.Text = settings.GetValue("File", string.Empty);
			this.compareTo.Text = settings.GetValue("CompareTo", string.Empty);
			this.algorithm.Text = settings.GetValue("Algorithm", this.algorithm.Text);
		}

		private void Saver_SaveSettings(object sender, SettingsEventArgs e)
		{
			var settings = e.SettingsNode;
			settings.SetValue("File", this.fileEdit.Text);
			settings.SetValue("CompareTo", this.compareTo.Text);
			settings.SetValue("Algorithm", this.algorithm.Text);
		}

		private void SelectFile_Click(object sender, RoutedEventArgs e)
		{
			OpenFileDialog dialog = new OpenFileDialog();
			dialog.Title = "Select File";
			dialog.FileName = this.fileEdit.Text;
			dialog.Filter = "All Files (*.*)|*.*";

			if (dialog.ShowDialog(this).GetValueOrDefault())
			{
				this.fileEdit.Text = dialog.FileName;
			}
		}

		private async void Start_ClickAsync(object sender, RoutedEventArgs e)
		{
			if (this.threadSafeStream != null)
			{
				this.Cancel();
			}
			else
			{
				await this.StartAsync();
			}
		}

		private void Window_DragOver(object sender, DragEventArgs e)
		{
			// Note: This handler is also used for DragEnter events to avoid a brief cursor flicker
			// when a drag enters each control.
			if (e.Data.GetDataPresent(DataFormats.FileDrop))
			{
				e.Effects = DragDropEffects.Copy;
			}
			else
			{
				e.Effects = DragDropEffects.None;
			}

			// We need to set this to Handled since we're also using this method for
			// TextBox.PreviewDragXxx events.  Otherwise, TextBox won't allow file drags.
			// http://social.msdn.microsoft.com/Forums/vstudio/en-US/a539c487-1dec-4935-b91b-c3ec252eb834/drop-file-in-a-textbox?forum=wpf
			e.Handled = true;
		}

		private void Window_Drop(object sender, DragEventArgs e)
		{
			if (e.Data.GetDataPresent(DataFormats.FileDrop))
			{
				string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
				if (files.Length > 0)
				{
					this.fileEdit.Text = files[0];
				}

				e.Handled = true;
			}
		}

		#endregion
	}
}
