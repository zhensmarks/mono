import re
import sys

def replace_panels(filepath):
    with open(filepath, 'r') as f:
        content = f.read()

    # COMMENT PANEL
    comment_panel_new = """        <!-- Side Panel: Comments -->
        <Border x:Name="Part_CommentPanel" Width="450" Background="#F00A0A0A" 
                BorderBrush="#22FFFFFF" BorderThickness="1,0,0,0"
                IsVisible="{Binding IsCommentPanelOpen}"
                PointerPressed="OnSubPanelPointerPressed">
            <Grid RowDefinitions="Auto, *, Auto" Margin="24">
                <Grid Grid.Row="0" ColumnDefinitions="*, Auto" Margin="0,0,0,24">
                    <StackPanel Spacing="4">
                         <TextBlock Text="COMMENTS" FontSize="18" FontWeight="Black" LetterSpacing="2" Foreground="White"/>
                         <TextBlock Text="{Binding SelectedCard.Name}" FontSize="11" FontWeight="Bold" Foreground="#666666" TextTrimming="CharacterEllipsis" MaxWidth="300" LetterSpacing="1"/>
                    </StackPanel>
                    <StackPanel Grid.Column="1" Orientation="Horizontal" Spacing="12">
                        <Button Click="OnOpenCommentWindowClicked" 
                                Background="Transparent" BorderThickness="0" Padding="4" Cursor="Hand" ToolTip.Tip="Open in New Window">
                            <PathIcon Data="{StaticResource IconOpenWindow}" Width="16" Height="16" Foreground="#888888"/>
                        </Button>
                        <Button Command="{Binding CloseCommentsPanelCommand}" Content="✕" 
                                Background="Transparent" BorderThickness="0" FontSize="16" FontWeight="Black" Foreground="#666666" Cursor="Hand">
                            <Button.Styles>
                                <Style Selector="Button:pointerover /template/ ContentPresenter">
                                    <Setter Property="Background" Value="Transparent"/>
                                    <Setter Property="Foreground" Value="White"/>
                                </Style>
                            </Button.Styles>
                        </Button>
                    </StackPanel>
                </Grid>

                <ScrollViewer x:Name="Part_CommentScrollViewer" Grid.Row="1">
                    <StackPanel Spacing="16">
                        <ProgressBar IsIndeterminate="True" IsVisible="{Binding IsLoadingComments}" Margin="0,24"/>
                        <TextBlock Text="NO LOGS FOUND." IsVisible="{Binding !Comments.Count}" 
                                   Foreground="#666666" FontWeight="Black" LetterSpacing="2" HorizontalAlignment="Center"/>
                        
                        <ItemsControl ItemsSource="{Binding Comments}">
                            <ItemsControl.ItemTemplate>
                                <DataTemplate>
                                    <Border BorderBrush="#33FFFFFF" BorderThickness="1,0,0,0" Padding="12,0,0,16" Margin="0,0,0,8">
                                        <Grid RowDefinitions="Auto, *">
                                            <!-- Header (Name, Date, Actions) -->
                                            <Grid Grid.Row="0" ColumnDefinitions="*, Auto" Margin="0,0,0,8">
                                                <StackPanel Orientation="Horizontal" Spacing="8" VerticalAlignment="Center">
                                                    <TextBlock Text="{Binding MemberCreatorName}" FontWeight="Black" FontSize="12" Foreground="White" VerticalAlignment="Center" LetterSpacing="1"/>
                                                    <TextBlock Text="{Binding Date, StringFormat='{}{0:dd MMM HH:mm}'}" FontSize="10" FontWeight="Bold" Foreground="#888888" VerticalAlignment="Center" Margin="0,1,0,0"/>
                                                </StackPanel>
                                                
                                                <!-- Action Bar -->
                                                <StackPanel Grid.Column="1" Orientation="Horizontal" Spacing="4" IsVisible="{Binding IsMine}">
                                                    <Button Command="{Binding $parent[UserControl].DataContext.BeginEditCommentCommand}" CommandParameter="{Binding}"
                                                            Background="Transparent" BorderThickness="0" Padding="4" Cursor="Hand" ToolTip.Tip="Edit Comment"
                                                            IsVisible="{Binding !IsEditing}">
                                                        <PathIcon Data="{StaticResource IconEdit}" Width="12" Height="12" Foreground="#888888"/>
                                                    </Button>
                                                    <Button Command="{Binding $parent[UserControl].DataContext.DeleteCommentCommand}" CommandParameter="{Binding}"
                                                            Background="Transparent" BorderThickness="0" Padding="4" Cursor="Hand" ToolTip.Tip="Delete Comment"
                                                            IsVisible="{Binding !IsEditing}">
                                                        <PathIcon Data="{StaticResource IconTrash}" Width="12" Height="12" Foreground="#EF4444"/>
                                                    </Button>
                                                </StackPanel>
                                            </Grid>

                                            <!-- Body -->
                                            <Grid Grid.Row="1">
                                                <!-- Read Mode -->
                                                <controls:FormattedTextBlock Text="{Binding Text}" Foreground="#BBBBBB" FontSize="13"
                                                                             IsVisible="{Binding !IsEditing}"/>
                                                
                                                <!-- Edit Mode -->
                                                <StackPanel IsVisible="{Binding IsEditing}" Spacing="8" Margin="0,4,0,0">
                                                    <TextBox Text="{Binding EditText}" AcceptsReturn="True" TextWrapping="Wrap" MinHeight="60"
                                                             Background="#0AFFFFFF" BorderBrush="#44FFFFFF" BorderThickness="0,0,0,1" CornerRadius="0"
                                                             Padding="8" FontSize="13" Foreground="White"/>
                                                    <StackPanel Orientation="Horizontal" Spacing="8" HorizontalAlignment="Right">
                                                        <Button Content="CANCEL" Command="{Binding $parent[UserControl].DataContext.CancelEditCommentCommand}" CommandParameter="{Binding}"
                                                                Background="Transparent" Foreground="#888888" Padding="16,8" CornerRadius="0" Cursor="Hand" FontSize="11" FontWeight="Black" LetterSpacing="1"/>
                                                        <Button Content="SAVE" Command="{Binding $parent[UserControl].DataContext.SaveEditCommentCommand}" CommandParameter="{Binding}"
                                                                Background="{DynamicResource AccentEditingBrush}" Foreground="White" Padding="16,8" CornerRadius="0" Cursor="Hand" FontSize="11" FontWeight="Black" LetterSpacing="1"/>
                                                    </StackPanel>
                                                </StackPanel>
                                            </Grid>
                                        </Grid>
                                    </Border>
                                </DataTemplate>
                            </ItemsControl.ItemTemplate>
                        </ItemsControl>
                    </StackPanel>
                </ScrollViewer>

                <Grid Grid.Row="2" Margin="0,24,0,0">
                    <StackPanel VerticalAlignment="Bottom" Spacing="8">

                        <!-- Pending Attachments Preview -->
                        <Border Background="#1AFFFFFF" BorderBrush="#33FFFFFF" BorderThickness="1,0,0,0"
                                CornerRadius="0" Padding="12" Margin="0,0,0,8"
                                IsVisible="{Binding HasPendingAttachments}">
                            <StackPanel Spacing="8">
                                <TextBlock Text="ATTACHMENTS" FontSize="10" FontWeight="Black" LetterSpacing="2" Foreground="#888888"/>
                                <ItemsControl ItemsSource="{Binding PendingAttachments}">
                                    <ItemsControl.ItemsPanel>
                                        <ItemsPanelTemplate>
                                            <WrapPanel Orientation="Horizontal"/>
                                        </ItemsPanelTemplate>
                                    </ItemsControl.ItemsPanel>
                                    <ItemsControl.ItemTemplate>
                                        <DataTemplate x:DataType="models:PendingAttachmentItem">
                                            <Border Background="#22FFFFFF" CornerRadius="0" Padding="4" Margin="0,0,8,8"
                                                    Width="80" Height="80" ClipToBounds="True" BorderBrush="#44FFFFFF" BorderThickness="1">
                                                <Grid>
                                                    <Image Source="{Binding Thumbnail}" Stretch="UniformToFill"
                                                           IsVisible="{Binding IsImage}" VerticalAlignment="Center" HorizontalAlignment="Center"/>
                                                    <StackPanel IsVisible="{Binding !IsImage}" VerticalAlignment="Center" HorizontalAlignment="Center" Spacing="4">
                                                        <PathIcon Data="{StaticResource IconFile}" Width="20" Height="20" Foreground="#888888"/>
                                                        <TextBlock Text="{Binding FileName}" FontSize="9" FontWeight="Bold" TextTrimming="CharacterEllipsis" 
                                                                   Foreground="#888888" HorizontalAlignment="Center" MaxWidth="70"/>
                                                    </StackPanel>
                                                    <Button Command="{Binding $parent[UserControl].DataContext.RemoveAttachmentCommand}" CommandParameter="{Binding}"
                                                            Width="20" Height="20" CornerRadius="0" Padding="0"
                                                            Background="#CC000000" BorderThickness="0" Cursor="Hand"
                                                            HorizontalAlignment="Right" VerticalAlignment="Top">
                                                        <TextBlock Text="✕" FontSize="10" FontWeight="Black" Foreground="White" HorizontalAlignment="Center" VerticalAlignment="Center"/>
                                                    </Button>
                                                </Grid>
                                            </Border>
                                        </DataTemplate>
                                    </ItemsControl.ItemTemplate>
                                </ItemsControl>
                            </StackPanel>
                        </Border>

                        <!-- Presets Overlay (Above Textbox) -->
                        <Border Background="#1AFFFFFF" BorderBrush="#33FFFFFF" BorderThickness="1,0,0,0"
                                CornerRadius="0" Padding="16" Margin="0,0,0,8"
                                IsVisible="{Binding IsPresetPanelOpen}">
                            <StackPanel Spacing="12">
                                 <TextBlock Text="QUICK REPLIES (MAX 5)" FontSize="10" FontWeight="Black" LetterSpacing="2" Foreground="#888888"/>
                                 
                                 <ItemsControl ItemsSource="{Binding CommentPresets}">
                                     <ItemsControl.ItemTemplate>
                                         <DataTemplate>
                                             <Grid ColumnDefinitions="*, Auto" Margin="0,0,0,8">
                                                 <Button Command="{Binding $parent[UserControl].DataContext.UsePresetCommand}" CommandParameter="{Binding}"
                                                         HorizontalAlignment="Stretch" HorizontalContentAlignment="Left"
                                                         Background="Transparent" BorderThickness="0" Padding="8" Cursor="Hand">
                                                     <TextBlock Text="{Binding}" TextTrimming="CharacterEllipsis" Foreground="White" FontSize="12" FontWeight="Bold"/>
                                                 </Button>
                                                 <Button Grid.Column="1" Command="{Binding $parent[UserControl].DataContext.DeleteCommentPresetCommand}" CommandParameter="{Binding}"
                                                         Background="Transparent" BorderThickness="0" Padding="8" Cursor="Hand">
                                                     <PathIcon Data="{StaticResource IconTrash}" Width="12" Height="12" Foreground="#EF4444"/>
                                                 </Button>
                                             </Grid>
                                         </DataTemplate>
                                     </ItemsControl.ItemTemplate>
                                 </ItemsControl>

                                 <!-- Add New Preset -->
                                 <Grid ColumnDefinitions="*, Auto" IsVisible="{Binding CommentPresets.Count, Converter={x:Static ObjectConverters.IsNotNull}}"> 
                                      <TextBox Text="{Binding NewPresetText}" Watermark="New preset... (Enter to save)" 
                                               Background="#0AFFFFFF" BorderBrush="#44FFFFFF" BorderThickness="0,0,0,1" CornerRadius="0" Padding="8" Margin="0,0,8,0" Foreground="White" FontSize="12">
                                          <TextBox.KeyBindings>
                                              <KeyBinding Gesture="Enter" Command="{Binding AddCommentPresetCommand}"/>
                                          </TextBox.KeyBindings>
                                      </TextBox>
                                      <Button Grid.Column="1" Command="{Binding AddCommentPresetCommand}"
                                              Background="Transparent" BorderThickness="0" Padding="8" Cursor="Hand" ToolTip.Tip="Save Preset">
                                          <PathIcon Data="{StaticResource IconCheck}" Width="14" Height="14" Foreground="#22C55E"/>
                                      </Button>
                                 </Grid>
                            </StackPanel>
                        </Border>

                        <!-- @Mention Popup -->
                        <Border Background="#1AFFFFFF" BorderBrush="#33FFFFFF" BorderThickness="1,0,0,0"
                                CornerRadius="0" Padding="8" Margin="0,0,0,4" MaxHeight="160"
                                IsVisible="{Binding IsMentionPopupOpen}">
                            <ScrollViewer>
                                <ItemsControl ItemsSource="{Binding FilteredMembers}">
                                    <ItemsControl.ItemTemplate>
                                        <DataTemplate x:DataType="models:TrelloMember">
                                            <Button Command="{Binding $parent[UserControl].DataContext.SelectMentionCommand}" CommandParameter="{Binding}"
                                                    HorizontalAlignment="Stretch" HorizontalContentAlignment="Left"
                                                    Background="Transparent" BorderThickness="0" Padding="8,6" Cursor="Hand">
                                                <StackPanel Orientation="Horizontal" Spacing="12">
                                                    <Border Width="24" Height="24" CornerRadius="0" Background="{DynamicResource AccentEditingBrush}">
                                                        <TextBlock Text="{Binding Initials}" HorizontalAlignment="Center" VerticalAlignment="Center" 
                                                                   Foreground="White" FontWeight="Black" FontSize="10" LetterSpacing="1"/>
                                                    </Border>
                                                    <TextBlock Text="{Binding DisplayText}" VerticalAlignment="Center" FontSize="12" FontWeight="Bold"
                                                               Foreground="White"/>
                                                </StackPanel>
                                            </Button>
                                        </DataTemplate>
                                    </ItemsControl.ItemTemplate>
                                </ItemsControl>
                            </ScrollViewer>
                        </Border>
    
                        <!-- Comment input: textbox + send share one row height -->
                        <Grid ColumnDefinitions="*,Auto" MinHeight="48">
                            <TextBox x:Name="Part_CommentTextBox" Grid.Column="0"
                                     Text="{Binding NewCommentText}" Watermark="Write a log entry... (@mention)" 
                                     Background="#0AFFFFFF" BorderBrush="#44FFFFFF" BorderThickness="0,0,0,1" CornerRadius="0" Padding="12"
                                     Margin="0,0,12,0" AcceptsReturn="True" TextWrapping="Wrap"
                                     MinHeight="48" MaxHeight="120" VerticalAlignment="Stretch" Foreground="White" FontSize="13">
                             <TextBox.KeyBindings>
                                 <KeyBinding Gesture="Enter" Command="{Binding SendCommentCommand}"/>
                             </TextBox.KeyBindings>
                             <TextBox.InnerLeftContent>
                                 <Button x:Name="Part_InlineAttachButton"
                                         Command="{Binding AddAttachmentCommand}"
                                         Background="Transparent" BorderThickness="0" Padding="12,0,4,0" Margin="0" Cursor="Hand"
                                         ToolTip.Tip="Attach Image">
                                     <PathIcon Data="{StaticResource IconPaperclip}" Width="16" Height="16" Foreground="#888888"/>
                                 </Button>
                             </TextBox.InnerLeftContent>
                             <TextBox.InnerRightContent>
                                  <Button Command="{Binding TogglePresetPanelCommand}" 
                                          Background="Transparent" BorderThickness="0" Padding="8" Margin="0,0,4,0" Cursor="Hand" ToolTip.Tip="Quick Replies">
                                      <PathIcon Data="{StaticResource IconChevronUp}" Width="14" Height="14" Foreground="#888888"/>
                                  </Button>
                             </TextBox.InnerRightContent>
                            </TextBox>
                            <Button Grid.Column="1" Command="{Binding SendCommentCommand}" 
                                    Classes="Clear" MinWidth="48" CornerRadius="0" Cursor="Hand" ToolTip.Tip="Send Comment"
                                    VerticalAlignment="Stretch" HorizontalAlignment="Stretch"
                                    HorizontalContentAlignment="Stretch" VerticalContentAlignment="Stretch">
                                <Border Background="{DynamicResource AccentEditingBrush}" HorizontalAlignment="Stretch" VerticalAlignment="Stretch">
                                    <PathIcon Data="{StaticResource IconSend}" Foreground="White" Width="16" Height="16" HorizontalAlignment="Center" VerticalAlignment="Center"/>
                                </Border>
                            </Button>
                        </Grid>
                    </StackPanel>
                </Grid>
            </Grid>
        </Border>"""

    content = re.sub(r'<!-- Side Panel: Comments -->.*?<!-- Side Panel: Checklist -->', 
                     comment_panel_new + '\n\n        <!-- Side Panel: Checklist -->', 
                     content, flags=re.DOTALL)
                     
    with open(filepath, 'w') as f:
        f.write(content)

if __name__ == '__main__':
    replace_panels('/home/abeng/BMachine.v2/src/BMachine.UI/Views/EditingCardListView.axaml')
