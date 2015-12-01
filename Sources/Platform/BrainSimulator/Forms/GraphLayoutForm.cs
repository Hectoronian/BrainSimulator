﻿using GoodAI.BrainSimulator.Nodes;
using GoodAI.BrainSimulator.NodeView;
using GoodAI.Core;
using GoodAI.Core.Configuration;
using GoodAI.Core.Execution;
using GoodAI.Core.Nodes;
using Graph;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using GoodAI.Core.Memory;
using GoodAI.BrainSimulator.Properties;
using GoodAI.Core.Utils;
using WeifenLuo.WinFormsUI.Docking;

namespace GoodAI.BrainSimulator.Forms
{
    public partial class GraphLayoutForm : DockContent
    {
        private readonly MainForm m_mainForm;

        public MyNodeGroup Target { get; private set; } 

        public GraphLayoutForm(MainForm mainForm, MyNodeGroup target)
        {
            InitializeComponent();
            m_mainForm = mainForm;
            Target = target;
            Text = target.Name;
            
            Desktop.CreateConnection = delegate()
            {
                return new MyNodeViewConnection();
            };            
        }

        private ToolStripDropDownButton CreateToolStripMenu(string menuName, Image menuIcon)
        {
            ToolStripDropDownButton newMenu = new ToolStripDropDownButton();

            newMenu.Alignment = ToolStripItemAlignment.Right;
            newMenu.DisplayStyle = ToolStripItemDisplayStyle.Image;
            newMenu.ImageAlign = ContentAlignment.MiddleRight;
            newMenu.Image = menuIcon;
            newMenu.Name = menuName;
            newMenu.ShowDropDownArrow = false;            
            newMenu.AutoSize = false;
            newMenu.ImageScaling = ToolStripItemImageScaling.None;
            newMenu.Size = new System.Drawing.Size(46, 36);            
            return newMenu;
        }

        public CategorySortingHat CategorizeEnabledNodes()
        {
            HashSet<string> enabledNodes = new HashSet<string>();

            if (Properties.Settings.Default.ToolBarNodes != null)
            {
                foreach (string nodeTypeName in Properties.Settings.Default.ToolBarNodes)
                {
                    enabledNodes.Add(nodeTypeName);
                }
            }

            var categorizer = new CategorySortingHat();

            foreach (MyNodeConfig nodeConfig in MyConfiguration.KnownNodes.Values)
            {
                if (nodeConfig.CanBeAdded && (enabledNodes.Contains(nodeConfig.NodeType.Name) || nodeConfig.IsBasicNode))
                {
                    categorizer.AddNodeAndCategory(nodeConfig);
                }
            }

            return categorizer;
        }

        public void InitToolBar()
        {
            nodesToolStrip.Items.Clear();

            CategorySortingHat categorizer = CategorizeEnabledNodes();

            foreach (NodeCategory category in categorizer.SortedCategories.Reverse())  // drop downs are added from the bottom
            {
                ToolStripDropDownButton toolbarDropDown = CreateToolStripMenu(category.Name, category.SmallImage);
                toolbarDropDown.Tag = category.Name;  // TODO(Premek): pass target drop down in a UiTag attribute
                toolbarDropDown.ToolTipText = category.Name;

                nodesToolStrip.Items.Add(toolbarDropDown);
            }

            foreach (MyNodeConfig nodeConfig in categorizer.Nodes)
            {
                AddNodeButtonToCategoryMenu(nodeConfig);
            }

            nodesToolStrip.Items.Add(new ToolStripSeparator());

            InitQuickToolBar(categorizer);
        }

        private void InitQuickToolBar(CategorySortingHat categorizer)
        {
            Settings settings = Settings.Default;
            if (settings.QuickToolBarNodes == null)
            {
                settings.QuickToolBarNodes = new StringCollection();
            }

            foreach (MyNodeConfig nodeConfig in categorizer.Nodes)
            {
                if (settings.QuickToolBarNodes.Contains(nodeConfig.NodeType.Name))
                    AddNodeButton(nodeConfig);
            }
        }
 
        private void GraphLayoutForm_Load(object sender, EventArgs e)
        {
            InitToolBar();

            LoadContentIntoDesktop();

            Desktop.CompatibilityStrategy = new MyIOStrategy();
            Desktop.ConnectionAdded += OnConnectionAdded;
            Desktop.ConnectionRemoved += OnConnectionRemoved;

            Desktop.PanZoomPerformed += Desktop_PanZoomPerformed;

            groupButtonPanel.Visible = Target != m_mainForm.Project.Network;

            m_mainForm.SimulationHandler.StateChanged += SimulationHandler_StateChanged;
            SimulationHandler_StateChanged(this, 
                new MySimulationHandler.StateEventArgs(m_mainForm.SimulationHandler.State, m_mainForm.SimulationHandler.State));
            
            m_mainForm.SimulationHandler.ProgressChanged += SimulationHandler_ProgressChanged;
        }

        void Desktop_PanZoomPerformed(object sender, GraphControl.PanZoomEventArgs e)
        {
            StoreLayoutProperties();
        }        

        private void addNodeButton_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;

            Type nodeType = (sender as ToolStripItem).Tag as Type;

            MyNodeView newNodeView = MyNodeView.CreateNodeView(nodeType, Desktop);

            var dataObject = new DataObject();
            dataObject.SetData(typeof(MyNodeView), newNodeView);  // required to get derived types from GetData

            DragDropEffects result = DoDragDrop(dataObject, DragDropEffects.Copy);
            if (result != DragDropEffects.Copy)
                return;

            MyNode newNode = m_mainForm.Project.CreateNode(nodeType);
            if (!TryAddChildNode(newNode))
            {
                m_mainForm.Project.Network.RemoveChild(newNode);
                Desktop.RemoveNode(newNodeView);
                return;
            }

            // TODO: Change to all transforms
            if (newNode is MyWorkingNode)
            {
                (newNode as MyWorkingNode).EnableDefaultTasks();
            }

            newNodeView.Node = newNode;
            newNodeView.UpdateView();
            newNodeView.OnEndDrag();

            EnterGraphLayout();
        }

        private bool TryAddChildNode(MyNode newNode)
        {
            try
            {
                Target.AddChild(newNode);
                return true;
            }
            catch (Exception e)
            {
                MyLog.ERROR.WriteLine("Failed to add node: " + e.Message);
                return false;
            }
        }

        void OnConnectionAdded(object sender, AcceptNodeConnectionEventArgs e)
        {
            MyNode fromNode = (e.Connection.From.Node as MyNodeView).Node;
            MyNode toNode = (e.Connection.To.Node as MyNodeView).Node;      

            int fromIndex = (int)e.Connection.From.Item.Tag;
            int toIndex = (int)e.Connection.To.Item.Tag;

            if (toNode.AcceptsConnection(fromNode, fromIndex, toIndex))
            {
                MyConnection newConnection = new MyConnection(fromNode, toNode, fromIndex, toIndex);
                newConnection.Connect();

                e.Connection.Tag = newConnection;

                m_mainForm.RefreshConnections(this);
            }
            else
            {
                // Make the graph library drop the connection.
                e.Cancel = true;
            }
        }

        void OnConnectionRemoved(object sender, NodeConnectionEventArgs e)
        {
            MyConnection connToDelete = e.Connection.Tag as MyConnection;

            if (connToDelete != null)
            {
                connToDelete.Disconnect();
            }

            m_mainForm.RefreshConnections(this);
        }

        private void desktop_FocusChanged(object sender, ElementEventArgs e)
        {            
            GraphLayoutForm_Enter(sender, EventArgs.Empty);
        }

        private void desktop_MouseEnter(object sender, EventArgs e)
        {
            //if (!Desktop.Focused) Desktop.Focus();
        }

        private void desktop_MouseLeave(object sender, EventArgs e)
        {
            if (Desktop.Focused) Desktop.Parent.Focus();
        }        

        private bool TestIfInsideSimulation()
        {
            if (m_mainForm.SimulationHandler.State != MySimulationHandler.SimulationState.STOPPED)
            {
                m_mainForm.PauseSimulationForAction(() =>
                {
                    MessageBox.Show("Not allowed during simulation", "Invalid operation", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                    return true;
                });                

                return true;
            }
            else return false;
        }

        private void Desktop_MouseDown(object sender, MouseEventArgs e)
        {
            if (!Desktop.Focused) Desktop.Focus();
        }

        private void Desktop_DoubleClick(object sender, EventArgs e)
        {
            MyNodeView nodeView = Desktop.FocusElement as MyNodeView;
            if (nodeView != null)
            {
                if (nodeView.Node is MyNodeGroup)
                {
                    m_mainForm.OpenGraphLayout(nodeView.Node as MyNodeGroup);
                }
                else if (nodeView.Node is MyScriptableNode)
                {
                    m_mainForm.OpenTextEditor(nodeView.Node as MyScriptableNode);
                }
            }            
        }

        private void Desktop_NodeRemoving(object sender, AcceptNodeEventArgs e)
        {
            e.Cancel = TestIfInsideSimulation();

            MyNodeView nodeView = e.Node as MyNodeView;
            e.Cancel |= nodeView.Node is MyParentInput || nodeView.Node is MyOutput;
        }

        private void Desktop_NodeRemoved(object sender, NodeEventArgs e)
        {
            MyNode node = (e.Node as MyNodeView).Node;
            if (node != null)
            {
                Target.RemoveChild(node);
            }

            if (node is MyNodeGroup)
            {
                m_mainForm.CloseGraphLayout(node as MyNodeGroup);                            
            }
            else if (node is MyScriptableNode)
            {
                m_mainForm.CloseTextEditor(node as MyScriptableNode);
            }

            m_mainForm.CloseObservers(node);
            m_mainForm.RemoveFromDashboard(node);
        }

        public void worldButton_Click(object sender, EventArgs e)
        {
            Desktop.FocusElement = null;
            MyWorld world = m_mainForm.Project.World;

            m_mainForm.NodePropertyView.Target = world;
            m_mainForm.MemoryBlocksView.Target = world;
            m_mainForm.TaskView.Target = world;
            m_mainForm.HelpView.Target = world;

            worldButtonPanel.BackColor = Color.DarkOrange;
            groupButtonPanel.BackColor = SystemColors.Control;            
        }

        private void Desktop_ConnectionRemoving(object sender, AcceptNodeConnectionEventArgs e)
        {
            e.Cancel = TestIfInsideSimulation();
        }

        private void Desktop_ConnectionAdding(object sender, AcceptNodeConnectionEventArgs e)
        {
            e.Cancel = TestIfInsideSimulation();
        }

        private void GraphLayoutForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            m_mainForm.SimulationHandler.StateChanged -= SimulationHandler_StateChanged;
            m_mainForm.SimulationHandler.ProgressChanged -= SimulationHandler_ProgressChanged;
        }

        private void GraphLayoutForm_Enter(object sender, EventArgs e)
        {
            EnterGraphLayout();
        }

        private void EnterGraphLayout()
        {
            if (Desktop.FocusElement is MyNodeView)
            {
                MyNode node = (Desktop.FocusElement as MyNodeView).Node;
                m_mainForm.NodePropertyView.Target = node;
                m_mainForm.MemoryBlocksView.Target = node;
                m_mainForm.HelpView.Target = node;

                if (node is MyWorkingNode)
                {
                    m_mainForm.TaskView.Target = node as MyWorkingNode;
                }
                else
                {
                    m_mainForm.TaskView.Target = null;
                }
            }
            else
            {
                m_mainForm.NodePropertyView.Target = null;
                m_mainForm.TaskView.Target = null;
                m_mainForm.MemoryBlocksView.Target = null;
                m_mainForm.HelpView.Target = null;
            }

            worldButtonPanel.BackColor = SystemColors.Control;
            groupButtonPanel.BackColor = SystemColors.Control;

            HideDropDownMenus();
        }

        private void HideDropDownMenus()
        {
            foreach (var dropDownButton in nodesToolStrip.Items.OfType<ToolStripDropDownButton>())
            {
                dropDownButton.DropDown.Hide();
            }
        }

        public void ReloadContent()
        {
            Desktop.ConnectionAdded -= OnConnectionAdded;
            Desktop.ConnectionRemoved -= OnConnectionRemoved;

            Desktop.NodeRemoving -= Desktop_NodeRemoving;
            Desktop.NodeRemoved -= Desktop_NodeRemoved;

            Desktop.FocusChanged -= desktop_FocusChanged;

            Desktop.RemoveNodes(Desktop.Nodes.ToList());
            LoadContentIntoDesktop();

            Desktop.FocusChanged += desktop_FocusChanged;

            Desktop.NodeRemoving += Desktop_NodeRemoving;
            Desktop.NodeRemoved += Desktop_NodeRemoved;
            
            Desktop.ConnectionAdded += OnConnectionAdded;
            Desktop.ConnectionRemoved += OnConnectionRemoved;
        }

        private void zoomButton_Click(object sender, EventArgs e)
        {
            Desktop.ZoomToBounds();
        }

        private void GraphLayoutForm_Shown(object sender, EventArgs e)
        {
            if (Target.LayoutProperties != null)
            {
                Desktop.SetZoomAndTranslation(Target.LayoutProperties.Zoom,
                    new PointF(Target.LayoutProperties.Translation.X, Target.LayoutProperties.Translation.Y));
            }
        }

        private void updateModelButton_Click(object sender, EventArgs e)
        {
            m_mainForm.SimulationHandler.UpdateMemoryModel();
            Desktop.Invalidate();
        }

        private void removeNodeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (sender is ToolStripItem)
            {
                RemoveNodeButton(contextMenuStrip.Tag as ToolStripItem);
            }
        }

        private void groupButton_Click(object sender, EventArgs e)
        {
            Desktop.FocusElement = null;
            MyNodeGroup group = this.Target;

            m_mainForm.NodePropertyView.Target = group;
            m_mainForm.MemoryBlocksView.Target = group;
            m_mainForm.TaskView.Target = group;
            m_mainForm.HelpView.Target = group;

            worldButtonPanel.BackColor = SystemColors.Control;
            groupButtonPanel.BackColor = Color.DarkOrange;            
        }

        private void desktopContextMenuStrip_Opened(object sender, EventArgs e)
        {
            searchTextBox.Focus();
        }

        public void RefreshGraph()
        {
            foreach (Node grahpNode in Desktop.Nodes)
            {
                var nodeView = grahpNode as MyNodeView;
                if (nodeView == null)
                    continue;

                // refresh node
                nodeView.UpdateView();

                // refresh connections
                foreach (NodeConnection connectionView in grahpNode.Connections)
                {
                    RefreshConnectionView(connectionView);
                }
            }

        }

        private static void RefreshConnectionView(NodeConnection connection)
        {
            var from = (connection.From.Node as MyNodeView).Node;
            var to = (connection.To.Node as MyNodeView).Node;
            var connectionView = (connection as MyNodeViewConnection);

            // If order == 0, the node is likely an output.
            connectionView.Backward = to.TopologicalOrder != 0 && @from.TopologicalOrder >= to.TopologicalOrder;

            // If order == 0, the node is likely an output.
            MyAbstractMemoryBlock output = from.GetAbstractOutput((int) connection.From.Item.Tag);
            if (output != null)
                connectionView.Dynamic = output.IsDynamic;
            else
                connectionView.Dynamic = false;
        }

        private void nodesToolStrip_DragEnter(object sender, DragEventArgs e)
        {
            if (CanAcceptNode(e.Data))
                e.Effect |= DragDropEffects.Copy;
            else
                e.Effect = DragDropEffects.None;
        }

        private void nodesToolStrip_DragDrop(object sender, DragEventArgs e)
        {
            MyNodeConfig nodeConfig;
            if (!CanAcceptNode(e.Data, out nodeConfig))
                return;

            AddQuickToolBarItem(nodeConfig);

            e.Effect = DragDropEffects.None;  // prevent creation of the actual node
        }

        private void AddQuickToolBarItem(MyNodeConfig nodeConfig)
        {
            AddNodeButton(nodeConfig);

            Settings.Default.QuickToolBarNodes.Add(nodeConfig.NodeType.Name);
        }

        private static bool CanAcceptNode(IDataObject data, out MyNodeConfig nodeConfig) 
        {
            nodeConfig = GetNodeConfigFromDropData(data);
            if (nodeConfig == null)
                return false;

            return !Settings.Default.QuickToolBarNodes.Contains(nodeConfig.NodeType.Name);
        }

        private static bool CanAcceptNode(IDataObject data)
        {
            MyNodeConfig ignoredConfig;
            return CanAcceptNode(data, out ignoredConfig);
        }

        private static MyNodeConfig GetNodeConfigFromDropData(IDataObject data)
        {
            var nodeView = data.GetData(typeof (MyNodeView)) as MyNodeView;

            return (nodeView == null) ? null : nodeView.Config;
        }
    }      
}
