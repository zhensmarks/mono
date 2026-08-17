import re

def replace_panels(filepath):
    with open(filepath, 'r') as f:
        content = f.read()

    checklist_panel_new = """        <!-- Side Panel: Checklist -->
        <Border x:Name="Part_ChecklistPanel" Width="450" Background="#F00A0A0A" 
                BorderBrush="#22FFFFFF" BorderThickness="1,0,0,0"
                IsVisible="{Binding IsChecklistPanelOpen}"
                PointerPressed="OnSubPanelPointerPressed">
            <Grid RowDefinitions="Auto, *, Auto" Margin="24">
                <Grid ColumnDefinitions="*, Auto" Margin="0,0,0,24">
                    <StackPanel Spacing="4">
                         <TextBlock Text="CHECKLISTS" FontSize="18" FontWeight="Black" LetterSpacing="2" Foreground="White"/>
                         <TextBlock Text="{Binding SelectedCard.Name}" FontSize="11" FontWeight="Bold" Foreground="#666666" TextTrimming="CharacterEllipsis" MaxWidth="300" LetterSpacing="1"/>
                    </StackPanel>
                    <Button Grid.Column="1" Command="{Binding CloseChecklistPanelCommand}" Content="✕" 
                            Background="Transparent" BorderThickness="0" FontSize="16" FontWeight="Black" Foreground="#666666" Cursor="Hand">
                         <Button.Styles>
                             <Style Selector="Button:pointerover /template/ ContentPresenter">
                                 <Setter Property="Background" Value="Transparent"/>
                                 <Setter Property="Foreground" Value="White"/>
                             </Style>
                         </Button.Styles>
                    </Button>
                </Grid>

                <ScrollViewer Grid.Row="1">
                    <StackPanel Spacing="24">
                        <ProgressBar IsIndeterminate="True" IsVisible="{Binding IsLoadingChecklists}" Margin="0,24"/>
                        <TextBlock Text="NO CHECKLISTS FOUND." IsVisible="{Binding !Checklists.Count}" 
                                   Foreground="#666666" FontWeight="Black" LetterSpacing="2" HorizontalAlignment="Center"/>
                        
                        <ItemsControl ItemsSource="{Binding Checklists}">
                            <ItemsControl.ItemTemplate>
                                <DataTemplate>
                                    <StackPanel Spacing="16" Margin="0,0,0,32">
                                        <Grid ColumnDefinitions="Auto, *, Auto" Background="#1AFFFFFF" Padding="12">
                                            <PathIcon Data="{StaticResource IconCheckSquare}" 
                                                  Foreground="White" Width="16" Height="16" VerticalAlignment="Center" Margin="0,0,12,0"/>
                                            <TextBlock Grid.Column="1" Text="{Binding Name}" FontWeight="Black" FontSize="13" Foreground="White" LetterSpacing="1" VerticalAlignment="Center"/>
                                            
                                            <Button Grid.Column="2" 
                                                    Command="{Binding $parent[UserControl].DataContext.DeleteChecklistCommand}" 
                                                    CommandParameter="{Binding}"
                                                    Background="Transparent" BorderThickness="0" Padding="8,4" Cursor="Hand" ToolTip.Tip="Delete Checklist">
                                                <PathIcon Data="{StaticResource IconTrash}" Width="14" Height="14" Foreground="#EF4444"/>
                                            </Button>
                                        </Grid>
                                        
                                        <ItemsControl ItemsSource="{Binding Items}" Margin="28,0,0,0">
                                            <ItemsControl.ItemTemplate>
                                                <DataTemplate>
                                                    <CheckBox IsChecked="{Binding IsChecked, Mode=TwoWay}" 
                                                              Content="{Binding Name}" Margin="0,4"
                                                              Command="{Binding $parent[UserControl].DataContext.ToggleCheckItemCommand}"
                                                              CommandParameter="{Binding}">
                                                        <CheckBox.Styles>
                                                            <Style Selector="CheckBox">
                                                                <Setter Property="Foreground" Value="#BBBBBB"/>
                                                                <Setter Property="FontSize" Value="13"/>
                                                            </Style>
                                                            <Style Selector="CheckBox:checked">
                                                                <Setter Property="Foreground" Value="#666666"/>
                                                            </Style>
                                                        </CheckBox.Styles>
                                                    </CheckBox>
                                                </DataTemplate>
                                            </ItemsControl.ItemTemplate>
                                        </ItemsControl>
                                    </StackPanel>
                                </DataTemplate>
                            </ItemsControl.ItemTemplate>
                        </ItemsControl>
                    </StackPanel>
                </ScrollViewer>
                
                <StackPanel Grid.Row="2" Margin="0,16,0,0">
                     <Border Background="#0AFFFFFF" BorderBrush="#44FFFFFF" BorderThickness="0,0,0,1" CornerRadius="0" Padding="20" IsVisible="{Binding IsDuplicateMode}" Margin="0,0,0,12">
                         <StackPanel Spacing="16">
                             <TextBlock Text="DUPLICATE CHECKLIST" FontWeight="Black" LetterSpacing="2" Foreground="White" FontSize="11"/>
                             <StackPanel Spacing="8">
                                  <TextBlock Text="NEW NAME" FontSize="10" FontWeight="Bold" LetterSpacing="1" Foreground="#666666"/>
                                  <TextBox Text="{Binding DuplicateChecklistName}" Watermark="Name" IsReadOnly="True" Background="Transparent" BorderThickness="0,0,0,1" BorderBrush="#44FFFFFF" Padding="8" Foreground="White" CornerRadius="0"/>
                             </StackPanel>
                             <StackPanel Spacing="8">
                                  <TextBlock Text="SOURCE" FontSize="10" FontWeight="Bold" LetterSpacing="1" Foreground="#666666"/>
                                   <AutoCompleteBox ItemsSource="{Binding Checklists}" 
                                                    SelectedItem="{Binding SelectedSourceChecklist}" 
                                                    ValueMemberBinding="{Binding Name}"
                                                    Watermark="Select source..." 
                                                    HorizontalAlignment="Stretch"
                                                    FilterMode="Contains"
                                                    MinimumPrefixLength="0"
                                                    GotFocus="AutoCompleteBox_GotFocus"
                                                    MaxDropDownHeight="160"
                                                    Background="#1AFFFFFF" BorderThickness="0" CornerRadius="0" Foreground="White" Padding="8"/>
                             </StackPanel>
                             <Grid ColumnDefinitions="*, *" Margin="0,8,0,0">
                                 <Button Grid.Column="0" Content="CANCEL" Command="{Binding ToggleDuplicateModeCommand}" 
                                         Background="Transparent" Foreground="#888888" HorizontalAlignment="Left" Padding="12,8" FontWeight="Black" LetterSpacing="1"/>
                                 <Button Grid.Column="1" Content="CREATE COPY" Command="{Binding DuplicateChecklistCommand}" 
                                         Background="{DynamicResource AccentEditingBrush}" Foreground="White" HorizontalAlignment="Right" Padding="12,8" FontWeight="Black" LetterSpacing="1" CornerRadius="0"/>
                             </Grid>
                         </StackPanel>
                     </Border>
                     <Button Command="{Binding ToggleDuplicateModeCommand}"
                             IsVisible="{Binding !IsDuplicateMode}"
                             IsEnabled="{Binding IsOnline}"
                             Background="Transparent" BorderBrush="{DynamicResource AccentEditingBrush}" BorderThickness="1" Foreground="{DynamicResource AccentEditingBrush}" 
                             HorizontalAlignment="Stretch" HorizontalContentAlignment="Center" CornerRadius="0" Padding="12" Cursor="Hand">
                         <StackPanel Orientation="Horizontal" Spacing="8" HorizontalAlignment="Center">
                             <PathIcon Data="{StaticResource IconPlus}" Width="12" Height="12" Foreground="{DynamicResource AccentEditingBrush}"/>
                             <TextBlock Text="DUPLICATE CHECKLIST" FontWeight="Black" LetterSpacing="2" FontSize="12"/>
                         </StackPanel>
                     </Button>
                </StackPanel>
            </Grid>
        </Border>



        <!-- Side Panel: Attachments -->
        <Border x:Name="Part_AttachmentPanel" Width="450" Background="#F00A0A0A" 
                BorderBrush="#22FFFFFF" BorderThickness="1,0,0,0"
                IsVisible="{Binding IsAttachmentPanelOpen}"
                PointerPressed="OnSubPanelPointerPressed">
            <Grid RowDefinitions="Auto, *, Auto" Margin="24">
                <Grid ColumnDefinitions="*, Auto" Margin="0,0,0,24">
                    <StackPanel Spacing="4">
                         <TextBlock Text="ATTACHMENTS" FontSize="18" FontWeight="Black" LetterSpacing="2" Foreground="White"/>
                         <TextBlock Text="{Binding SelectedCard.Name}" FontSize="11" FontWeight="Bold" Foreground="#666666" TextTrimming="CharacterEllipsis" MaxWidth="300" LetterSpacing="1"/>
                    </StackPanel>
                    <Button Grid.Column="1" Command="{Binding CloseAttachmentPanelCommand}" Content="✕" 
                            Background="Transparent" BorderThickness="0" FontSize="16" FontWeight="Black" Foreground="#666666" Cursor="Hand">
                         <Button.Styles>
                             <Style Selector="Button:pointerover /template/ ContentPresenter">
                                 <Setter Property="Background" Value="Transparent"/>
                                 <Setter Property="Foreground" Value="White"/>
                             </Style>
                         </Button.Styles>
                    </Button>
                </Grid>

                <ScrollViewer Grid.Row="1">
                    <StackPanel Spacing="24">
                        <ProgressBar IsIndeterminate="True" IsVisible="{Binding IsLoadingAttachments}" Margin="0,24"/>
                        <TextBlock Text="NO ATTACHMENTS." IsVisible="{Binding !Attachments.Count}" 
                                   Foreground="#666666" FontWeight="Black" LetterSpacing="2" HorizontalAlignment="Center"/>
                        
                        <ItemsControl ItemsSource="{Binding Attachments}">
                             <ItemsControl.ItemsPanel>
                                <ItemsPanelTemplate>
                                    <WrapPanel ItemWidth="180" ItemHeight="180"/>
                                </ItemsPanelTemplate>
                             </ItemsControl.ItemsPanel>
                            <ItemsControl.ItemTemplate>
                                <DataTemplate>
                                    <Border Margin="8" Background="#1AFFFFFF" CornerRadius="0" ClipToBounds="True" BorderBrush="#33FFFFFF" BorderThickness="1">
                                        <Grid RowDefinitions="*, Auto">
                                            <!-- Preview: Clickable to Open -->
                                            <Button Grid.Row="0" Command="{Binding $parent[UserControl].DataContext.OpenAttachmentCommand}" CommandParameter="{Binding}"
                                                    Background="Transparent" BorderThickness="0" Padding="0" Margin="0" HorizontalAlignment="Stretch" VerticalAlignment="Stretch">
                                                <Panel>
                                                    <Image Source="{Binding Thumbnail}" IsVisible="{Binding IsImage}" Stretch="UniformToFill"/>
                                                    <PathIcon IsVisible="{Binding !IsImage}" Data="{StaticResource IconFile}" 
                                                          Foreground="#888888" Width="48" Height="48" HorizontalAlignment="Center" VerticalAlignment="Center" Opacity="0.5"/>
                                                </Panel>
                                            </Button>
                                            
                                            <!-- Download Overlay (Top Right) -->
                                            <Button Grid.Row="0" VerticalAlignment="Top" HorizontalAlignment="Right" Background="#AA000000" CornerRadius="0" Margin="8" Padding="6"
                                                    Command="{Binding $parent[UserControl].DataContext.DownloadAttachmentCommand}" CommandParameter="{Binding}"
                                                    ToolTip.Tip="Download File">
                                                 <PathIcon Data="{StaticResource IconDownload}" Foreground="White" Width="14" Height="14"/>
                                            </Button> 
                                            
                                            <!-- Download Progress Overlay -->
                                            <Grid Grid.Row="0" Background="#99000000" IsVisible="{Binding IsDownloading}">
                                                 <ProgressBar IsIndeterminate="True" VerticalAlignment="Center" HorizontalAlignment="Center"/>
                                            </Grid>

                                            <!-- Metadata -->
                                            <Border Grid.Row="1" Background="#E6000000" Padding="12,10" BorderBrush="#33FFFFFF" BorderThickness="0,1,0,0">
                                                <StackPanel Spacing="2">
                                                    <TextBlock Text="{Binding Name}" FontWeight="Bold" Foreground="White" FontSize="11" TextTrimming="CharacterEllipsis" ToolTip.Tip="{Binding Name}"/>
                                                    <TextBlock Text="{Binding Bytes}" FontWeight="Bold" Foreground="#888888" FontSize="9"/>
                                                </StackPanel>
                                            </Border>
                                        </Grid>
                                    </Border>
                                </DataTemplate>
                            </ItemsControl.ItemTemplate>
                        </ItemsControl>
                    </StackPanel>
                </ScrollViewer>
            </Grid>
        </Border>
"""

    content = re.sub(r'<!-- Side Panel: Checklist -->.*?</Grid>\s*<!-- End Part_PanelsContainer -->', 
                     checklist_panel_new + '\n        </Grid> <!-- End Part_PanelsContainer -->', 
                     content, flags=re.DOTALL)
                     
    with open(filepath, 'w') as f:
        f.write(content)

if __name__ == '__main__':
    replace_panels('/home/abeng/BMachine.v2/src/BMachine.UI/Views/EditingCardListView.axaml')
