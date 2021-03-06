using Eruru.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace WorldOfTanks {

	public partial class OujBoxClanQueryForm : Form {

		readonly ListViewComparer MemberListViewComparer;

		string[] Modes;
		ListViewItem.ListViewSubItem CurrentSubItem;

		public OujBoxClanQueryForm () {
			InitializeComponent ();
			MemberListViewComparer = new ListViewComparer (MemberResultListView) {
				OnGetColumnType = column => {
					if (column == NameColumnHeader.Index) {
						return typeof (string);
					}
					if (column == PositionColumnHeader.Index) {
						return typeof (ClanMemberPosition);
					}
					return typeof (float);
				},
				OnSorted = () => {
					int serialNumber = 0;
					foreach (ListViewItem item in MemberListViewComparer.ListView.Items) {
						serialNumber++;
						item.Text = serialNumber.ToString ();
					}
				}
			};
			MemberResultListView.ListViewItemSorter = MemberListViewComparer;
			ModeComboBox.SelectedIndex = 0;
			NameTextBox.Text = Config.Instance.BoxClanQueryName;
			AutoResizeResultListViewColumns ();
			Api.AutoResizeListViewColumns (MemberResultListView);
		}

		private void QueryButton_Click (object sender, EventArgs e) {
			Query (NameTextBox.Text, false);
		}

		private void AttendanceButton_Click (object sender, EventArgs e) {
			if (MessageBox.Show ("使用此功能会发送大量请求，由于限制了频率，处理时间会很久，需要耐心等待，是否继续？", "注意", MessageBoxButtons.YesNo) != DialogResult.Yes) {
				return;
			}
			if (Api.CheckDateTime (StartDateTimePicker.Value, EndDateTimePicker.Value)) {
				Query (NameTextBox.Text, true);
			}
		}

		private void MemberResultListView_MouseClick (object sender, MouseEventArgs e) {
			if (e.Button == MouseButtons.Right) {
				CurrentSubItem = MemberResultListView.HitTest (e.X, e.Y).SubItem;
				if (CurrentSubItem == null) {
					return;
				}
				contextMenuStrip1.Show (MemberResultListView, e.Location);
			}
		}

		private void CopyToolStripMenuItem_Click (object sender, EventArgs e) {
			Clipboard.SetText (CurrentSubItem.Text);
		}

		private void ExportButton_Click (object sender, EventArgs e) {
			if (saveFileDialog1.ShowDialog () == DialogResult.OK) {
				Api.ExportCSV (ResultListView, MemberResultListView, saveFileDialog1.FileName);
			}
		}

		private void Control_KeyUp (object sender, KeyEventArgs e) {
			if (e.KeyCode == Keys.Enter) {
				QueryButton.PerformClick ();
			}
		}

		private void StartDateTimePicker_ValueChanged (object sender, EventArgs e) {
			if (EndDateTimePicker.Value < StartDateTimePicker.Value) {
				EndDateTimePicker.Value = StartDateTimePicker.Value;
			}
		}

		private void EndDateTimePicker_ValueChanged (object sender, EventArgs e) {
			if (StartDateTimePicker.Value > EndDateTimePicker.Value) {
				StartDateTimePicker.Value = EndDateTimePicker.Value;
			}
		}

		private void MemberResultListView_ColumnClick (object sender, ColumnClickEventArgs e) {
			MemberListViewComparer.OnClickColumn (e.Column);
		}

		private void ListView_DrawColumnHeader (object sender, DrawListViewColumnHeaderEventArgs e) {
			e.DrawDefault = true;
		}

		private void ListView_DrawSubItem (object sender, DrawListViewSubItemEventArgs e) {
			e.DrawDefault = true;
			if (e.SubItem.Tag != null) {
				DoubleColor doubleColor = (DoubleColor)e.SubItem.Tag;
				e.SubItem.ForeColor = doubleColor.ForeColor;
				e.SubItem.BackColor = doubleColor.BackColor;
			}
		}

		void AutoResizeResultListViewColumns () {
			Api.AutoResizeListViewColumns (ResultListView, true);
			MemberResultListView.Left = ResultListView.Right + 5;
			MemberResultListView.Width = AttendanceButton.Right - MemberResultListView.Left;
		}

		void SetState (string text) {
			Invoke (new Action (() => StateLabel.Text = text));
		}

		void Query (string name, bool isAttendance) {
			NameTextBox.Enabled = false;
			StartDateTimePicker.Enabled = false;
			EndDateTimePicker.Enabled = false;
			QueryButton.Enabled = false;
			AttendanceButton.Enabled = false;
			ExportButton.Enabled = false;
			ModeComboBox.Enabled = false;
			Modes = new string[] { ModeComboBox.Text };
			ResultListView.Items.Clear ();
			MemberResultListView.Items.Clear ();
			Config.Instance.BoxClanQueryName = NameTextBox.Text;
			ConfigDao.Instance.Save (Config.Instance);
			int count = 0;
			object summaryLock = new object ();
			AutoResetEvent autoResetEvent = new AutoResetEvent (false);
			new Thread (() => {
				try {
					int clanID = WarGamingNetService.Instance.GetClanIdByName (name);
					List<float> combats = new List<float> ();
					List<float> winRates = new List<float> ();
					List<float> hitRates = new List<float> ();
					List<float> combatLevels = new List<float> ();
					List<float> damages = new List<float> ();
					float totalCombat = 0;
					float totalWinRate = 0;
					float totalHitRate = 0;
					float totalCombatLevel = 0;
					float totalDamage = 0;
					List<ClanMember> clanMembers = WarGamingNetService.Instance.GetPlayersByClanId (clanID);
					List<ClanMember> pageOutClanMembers = new List<ClanMember> ();
					count = clanMembers.Count;
					SetState ($"进度：0% 0/{clanMembers.Count}");
					foreach (ClanMember clanMember in clanMembers) {
						ThreadPool.QueueUserWorkItem (state => {
							ClanMember innerClanMember = (ClanMember)state;
							try {
								try {
									innerClanMember.BoxPersonalCombatRecord = BoxService.Instance.GetPersonalCombatRecord (innerClanMember.Name);
								} catch {
									innerClanMember.BoxPersonalCombatRecord = new BoxPersonalCombatRecord () {
										Name = innerClanMember.Name
									};
									throw;
								}
								innerClanMember.AttendanceDaysText = "0";
								innerClanMember.AttendanceCountText = "0";
								combats.Add (innerClanMember.BoxPersonalCombatRecord.Combat);
								winRates.Add (innerClanMember.BoxPersonalCombatRecord.WinRate);
								hitRates.Add (innerClanMember.BoxPersonalCombatRecord.HitRate);
								combatLevels.Add (innerClanMember.BoxPersonalCombatRecord.CombatLevel);
								damages.Add (innerClanMember.BoxPersonalCombatRecord.Damage);
								totalCombat += innerClanMember.BoxPersonalCombatRecord.Combat;
								totalWinRate += innerClanMember.BoxPersonalCombatRecord.WinRate;
								totalHitRate += innerClanMember.BoxPersonalCombatRecord.HitRate;
								totalCombatLevel += innerClanMember.BoxPersonalCombatRecord.CombatLevel;
								totalDamage += innerClanMember.BoxPersonalCombatRecord.Damage;
								if (isAttendance) {
									try {
										innerClanMember.AttendanceDays = QueryAttendance (innerClanMember, out int attendanceCount, out int onlineDays, pageOutClanMember => {
											pageOutClanMembers.Add (pageOutClanMember);
										});
										innerClanMember.AttendanceDaysText = innerClanMember.AttendanceDays.ToString ();
										innerClanMember.AttendanceCount = attendanceCount;
										innerClanMember.AttendanceCountText = innerClanMember.AttendanceCount.ToString ();
										innerClanMember.OnlineDays = onlineDays;
									} catch (Exception e) {
										innerClanMember.AttendanceDaysText = e.ToString ();
									}
								}
							} catch (Exception exception) {
								innerClanMember.BoxPersonalCombatRecord.CombatText = exception.Message;
							} finally {
								lock (summaryLock) {
									count--;
									SetState ($"进度：{Api.Divide (clanMembers.Count - count, clanMembers.Count):P0} {clanMembers.Count - count}/{clanMembers.Count}");
									if (count <= 0) {
										autoResetEvent.Set ();
									}
								}
							}
						}, clanMember);
					}
					if (count <= 0) {
						autoResetEvent.Set ();
					}
					autoResetEvent.WaitOne ();
					float averageCombat = Api.Divide (totalCombat, combats.Count);
					float averageWinRate = Api.Divide (totalWinRate, combats.Count);
					float averageHitRate = Api.Divide (totalHitRate, combats.Count);
					float averageCombatLevel = Api.Divide (totalCombatLevel, combats.Count);
					float averageDamage = Api.Divide (totalDamage, combats.Count);
					combats.Sort ();
					float medianCombat = Api.GetMedian (combats);
					winRates.Sort ();
					float medianWinRate = Api.GetMedian (winRates);
					hitRates.Sort ();
					float medianHitRate = Api.GetMedian (hitRates);
					combatLevels.Sort ();
					float medianCombatLevel = Api.GetMedian (combatLevels);
					damages.Sort ();
					float medianDamage = Api.GetMedian (damages);
					Invoke (new Action (() => {
						ResultListView.BeginUpdate ();
						ResultListView.Items.Add ("军团").SubItems.Add ($"{name}");
						if (!isAttendance || StartDateTimePicker.Value.Date == EndDateTimePicker.Value.Date) {
							ResultListView.Items.Add ($"查询日期").SubItems.Add ($"{StartDateTimePicker.Value:yyyy年MM月dd日}");
						} else {
							ResultListView.Items.Add ($"查询范围：从").SubItems.Add ($"{StartDateTimePicker.Value:yyyy年MM月dd日}");
							ResultListView.Items.Add ($"至").SubItems.Add ($"{EndDateTimePicker.Value:yyyy年MM月dd日}");
						}
						ResultListView.Items.Add ("成员数").SubItems.Add ($"{clanMembers.Count}");
						ResultListView.Items.Add ("平均效率").SubItems.Add (new ListViewItem.ListViewSubItem () {
							Text = $"{averageCombat:F2}",
							Tag = Api.GetCombatColor (averageCombat)
						});
						ResultListView.Items.Add ("效率中位数").SubItems.Add (new ListViewItem.ListViewSubItem () {
							Text = $"{medianCombat:F2}",
							Tag = Api.GetCombatColor (medianCombat)
						});
						ResultListView.Items.Add ("平均胜率").SubItems.Add (new ListViewItem.ListViewSubItem () { Text = $"{averageWinRate:P2}" });
						ResultListView.Items.Add ("胜率中位数").SubItems.Add (new ListViewItem.ListViewSubItem () { Text = $"{medianWinRate:P2}" });
						ResultListView.Items.Add ("平均命中率").SubItems.Add (new ListViewItem.ListViewSubItem () { Text = $"{averageHitRate:P2}" });
						ResultListView.Items.Add ("命中率中位数").SubItems.Add (new ListViewItem.ListViewSubItem () { Text = $"{medianHitRate:P2}" });
						ResultListView.Items.Add ("平均出战等级").SubItems.Add (new ListViewItem.ListViewSubItem () { Text = $"{averageCombatLevel:F2}" });
						ResultListView.Items.Add ("出战等级中位数").SubItems.Add (new ListViewItem.ListViewSubItem () { Text = $"{medianCombatLevel:F2}" });
						ResultListView.Items.Add ("平均均伤").SubItems.Add (new ListViewItem.ListViewSubItem () { Text = $"{averageDamage:F2}" });
						ResultListView.Items.Add ("均伤中位数").SubItems.Add (new ListViewItem.ListViewSubItem () { Text = $"{medianDamage:F2}" });
						AutoResizeResultListViewColumns ();
						ResultListView.EndUpdate ();
						MemberResultListView.BeginUpdate ();
						int serialNumber = 0;
						foreach (ClanMember clanMember in clanMembers) {
							serialNumber++;
							ListViewItem listViewItem = new ListViewItem (serialNumber.ToString ()) {
								Tag = clanMember
							};
							listViewItem.SubItems.Insert (NameColumnHeader.Index, new ListViewItem.ListViewSubItem () { Text = clanMember.Name });
							listViewItem.SubItems.Insert (PositionColumnHeader.Index, new ListViewItem.ListViewSubItem () { Text = clanMember.Position });
							listViewItem.SubItems.Insert (CombatColumnHeader.Index, new ListViewItem.ListViewSubItem () {
								Text = clanMember.BoxPersonalCombatRecord.CombatText,
								Tag = Api.GetCombatColor (clanMember.BoxPersonalCombatRecord.Combat)
							});
							listViewItem.SubItems.Insert (WinRateColumnHeader.Index, new ListViewItem.ListViewSubItem () { Text = $"{clanMember.BoxPersonalCombatRecord.WinRate:P0}" });
							listViewItem.SubItems.Insert (HitRateColumnHeader.Index, new ListViewItem.ListViewSubItem () { Text = $"{clanMember.BoxPersonalCombatRecord.HitRate:P0}" });
							listViewItem.SubItems.Insert (AverageCombatLevelColumnHeader.Index, new ListViewItem.ListViewSubItem () { Text = $"{clanMember.BoxPersonalCombatRecord.CombatLevel}" });
							listViewItem.SubItems.Insert (AverageDamageColumnHeader.Index, new ListViewItem.ListViewSubItem () { Text = $"{clanMember.BoxPersonalCombatRecord.Damage}" });
							listViewItem.SubItems.Insert (OnlineDaysColumnHeader.Index, new ListViewItem.ListViewSubItem () { Text = $"{clanMember.OnlineDays}" });
							listViewItem.SubItems.Insert (AttendanceDaysColumnHeader.Index, new ListViewItem.ListViewSubItem () { Text = clanMember.AttendanceDaysText });
							listViewItem.SubItems.Insert (AttendanceCountColumnHeader.Index, new ListViewItem.ListViewSubItem () { Text = clanMember.AttendanceCountText });
							MemberResultListView.Items.Add (listViewItem);
						}
						Api.AutoResizeListViewColumns (MemberResultListView);
						MemberListViewComparer.SortColumn (CombatColumnHeader.Index, SortOrder.Descending);
						MemberResultListView.EndUpdate ();
						if (pageOutClanMembers.Count > 0) {
							StringBuilder stringBuilder = new StringBuilder ("以下成员由于偶游盒子战斗日志只显示7条*215页战斗日志，可能已丢失战斗日志，需要手动补充出勤次数：");
							for (int i = 0; i < pageOutClanMembers.Count; i++) {
								stringBuilder.AppendLine ();
								stringBuilder.Append (pageOutClanMembers[i].Name);
							}
							MessageBox.Show (stringBuilder.ToString ());
						}
					}));
				} catch (Exception e) {
					Invoke (new Action (() => {
						MessageBox.Show (this, e.ToString ());
					}));
				} finally {
					Invoke (new Action (() => {
						NameTextBox.Enabled = true;
						StartDateTimePicker.Enabled = true;
						EndDateTimePicker.Enabled = true;
						QueryButton.Enabled = true;
						AttendanceButton.Enabled = true;
						ExportButton.Enabled = true;
						StateLabel.Text = string.Empty;
						ModeComboBox.Enabled = true;
					}));
				}
			}) {
				IsBackground = true
			}.Start ();
		}

		/// <summary>
		/// 查询出勤，返回天数
		/// </summary>
		/// <param name="clanMember"></param>
		/// <param name="count">出勤次数</param>
		/// <param name="onlineDays">在线天数</param>
		/// <param name="onPageOut"></param>
		/// <returns></returns>
		int QueryAttendance (ClanMember clanMember, out int count, out int onlineDays, Action<ClanMember> onPageOut = null) {
			DateTime minDateTime = DateTime.Now;
			onlineDays = 0;
			try {
				BoxService.Instance.GetCombatRecords (clanMember.BoxPersonalCombatRecord, 215, ref minDateTime, out _, out _);
				if (minDateTime.Date >= StartDateTimePicker.Value.Date) {
					Console.WriteLine ($"考勤超出盒子战斗日志记录页数，需要手动考勤：{clanMember.Name}");
				}
			} catch {

			}
			List<BoxCombatRecord> combatRecords = BoxService.Instance.GetCombatRecords (
				clanMember.BoxPersonalCombatRecord,
				StartDateTimePicker.Value,
				EndDateTimePicker.Value,
				false,
				OnPageOut: () => onPageOut?.Invoke (clanMember)
			);
			using (AutoResetEvent autoResetEvent = new AutoResetEvent (false)) {
				object summaryLock = new object ();
				int threadCount = combatRecords.Count;
				Exception exception = null;
				foreach (BoxCombatRecord combatRecord in combatRecords) {
					ThreadPool.QueueUserWorkItem (state => {
						try {
							BoxCombatRecord innerBoxCombatRecord = (BoxCombatRecord)state;
							BoxService.Instance.FillCombatRecord (innerBoxCombatRecord);
							if (innerBoxCombatRecord.DateTime.Hour <= 2 && Modes.Contains (combatRecord.Mode)) {
								innerBoxCombatRecord.DateTime = innerBoxCombatRecord.DateTime.AddDays (-1);
							}
						} catch (Exception innerException) {
							exception = innerException;
						} finally {
							lock (summaryLock) {
								threadCount--;
								if (threadCount <= 0) {
									autoResetEvent.Set ();
								}
							}
						}
					}, combatRecord);
				}
				if (exception != null) {
					throw exception;
				}
				if (threadCount <= 0) {
					autoResetEvent.Set ();
				}
				autoResetEvent.WaitOne ();
			}
			DateTime dateTime = DateTime.MinValue;
			DateTime onlineDaysDateTime = DateTime.MinValue;
			List<int> days = new List<int> ();
			foreach (BoxCombatRecord combatRecord in combatRecords) {
				if (combatRecord.DateTime.Date >= StartDateTimePicker.Value.Date && combatRecord.DateTime.Date <= EndDateTimePicker.Value.Date) {

				} else {
					continue;
				}
				if (onlineDaysDateTime.Date != combatRecord.DateTime.Date) {
					if (combatRecord.DateTime.Hour >= 20) {
						onlineDaysDateTime = combatRecord.DateTime;
						onlineDays++;
					}
				}
				if (!Modes.Contains (combatRecord.Mode)) {
					continue;
				}
				if (dateTime.Date != combatRecord.DateTime.Date) {
					dateTime = combatRecord.DateTime;
					days.Add (0);
				}
				/*过0点
				if (dateTime.Date == combatRecord.DateTime.Date) {
					if (days[dayIndex] > 0) {
						
					}
				} else {
					dateTime = combatRecord.DateTime;
					dayIndex = days.Count;
					days.Add (0);
				}
				*/
				if (ValidateClanCombat ()) {
					if (days.Count > 0) {
						days[days.Count - 1]++;
					}
				}
				bool ValidateClanCombat () {
					int clanMemberNumber = 0;
					foreach (JsonValue playerJsonObject in combatRecord.PlayerTeamPlayers) {
						if (playerJsonObject["clanDBID"] == clanMember.ClanID) {
							clanMemberNumber++;
						}
					}
					if (clanMemberNumber >= 5 && (Math.Max (combatRecord.TeamAPlayers.Count, combatRecord.TeamBPlayers.Count) >= 10)) {
						return true;
					}
					return false;
				}
			}
			count = days.Sum ();
			return days.Sum (value => value > 0 ? 1 : 0);
		}

	}

}