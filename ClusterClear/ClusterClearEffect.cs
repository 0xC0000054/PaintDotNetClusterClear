﻿// Compiler options:  /unsafe /optimize /debug- /target:library /out:"C:\Users\edwar\Desktop\ClusterClear.dll"
using System;
using System.Runtime;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using System.Drawing.Text;
using System.Windows.Forms;
using System.IO.Compression;
using System.Drawing.Drawing2D;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using Registry = Microsoft.Win32.Registry;
using RegistryKey = Microsoft.Win32.RegistryKey;
using PaintDotNet;
using PaintDotNet.Effects;
using PaintDotNet.AppModel;
using PaintDotNet.IndirectUI;
using PaintDotNet.Collections;
using PaintDotNet.PropertySystem;

using IntSliderControl = System.Int32;
using CheckboxControl = System.Boolean;
using ColorWheelControl = PaintDotNet.ColorBgra;
using AngleControl = System.Double;
using PanSliderControl = PaintDotNet.Pair<double,double>;
using TextboxControl = System.String;
using DoubleSliderControl = System.Double;
using ListBoxControl = System.Byte;
using RadioButtonControl = System.Byte;
using ReseedButtonControl = System.Byte;
using MultiLineTextboxControl = System.String;
using RollControl = System.Tuple<double,double,double>;

[assembly: AssemblyTitle("ClusterClear Plugin for Paint.NET")]
[assembly: AssemblyDescription("Cluster Clear selected pixels")]
[assembly: AssemblyConfiguration("cluster clear")]
[assembly: AssemblyCompany("")]
[assembly: AssemblyProduct("ClusterClear")]
[assembly: AssemblyCopyright("Copyright © ")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]
[assembly: ComVisible(false)]
[assembly: AssemblyVersion("1.0.0")]

namespace ClusterClearEffect {
	public class PluginSupportInfo:IPluginSupportInfo {
		public string Author {
			get {
				return ((AssemblyCopyrightAttribute)base.GetType().Assembly.GetCustomAttributes(typeof(AssemblyCopyrightAttribute),false)[0]).Copyright;
			}
		}
		public string Copyright {
			get {
				return ((AssemblyDescriptionAttribute)base.GetType().Assembly.GetCustomAttributes(typeof(AssemblyDescriptionAttribute),false)[0]).Description;
			}
		}

		public string DisplayName {
			get {
				return ((AssemblyProductAttribute)base.GetType().Assembly.GetCustomAttributes(typeof(AssemblyProductAttribute),false)[0]).Product;
			}
		}

		public Version Version {
			get {
				return base.GetType().Assembly.GetName().Version;
			}
		}

		public Uri WebsiteUri {
			get {
				return new Uri("http://www.getpaint.net/redirect/plugins.html");
			}
		}
	}

	[PluginSupportInfo(typeof(PluginSupportInfo),DisplayName = "Cluster Clear")]
	public class ClusterClearEffectPlugin:PropertyBasedEffect {
		public static string StaticName {
			get {
				return "Cluster Clear";
			}
		}

		public static Image StaticIcon {
			get {
				return null;
			}
		}

		public static string SubmenuName {
			get {
				return "Object";
			}
		}

		public ClusterClearEffectPlugin()
			: base(StaticName,StaticIcon,SubmenuName,EffectFlags.Configurable) {
		}

		public enum PropertyNames {
			LowerThreshold,
			UpperThreshold,
			Tolerance
		}


		protected override PropertyCollection OnCreatePropertyCollection() {
			List<Property> props = new List<Property>();
			props.Add(new Int32Property(PropertyNames.LowerThreshold,0,0,300));
			props.Add(new Int32Property(PropertyNames.UpperThreshold,150,0,500));
			props.Add(new Int32Property(PropertyNames.Tolerance,((int)(toleranceMax*0.988)),0,(int)toleranceMax));

			return new PropertyCollection(props);
		}

		protected override ControlInfo OnCreateConfigUI(PropertyCollection props) {
			ControlInfo configUI = CreateDefaultConfigUI(props);
			configUI.SetPropertyControlValue(PropertyNames.LowerThreshold,ControlInfoPropertyNames.DisplayName,"Cluster Size Lower Threshold");
			configUI.SetPropertyControlValue(PropertyNames.UpperThreshold,ControlInfoPropertyNames.DisplayName,"Cluster Size Upper Threshold");
			configUI.SetPropertyControlValue(PropertyNames.Tolerance,ControlInfoPropertyNames.DisplayName,"Tolerance ‰");

			return configUI;
		}

		static readonly float toleranceMax = 1000;
		bool ClustersFinished=false;
		bool ToleranceChanged=true;
		List<Cluster> clusters;
		protected override void OnSetRenderInfo(PropertyBasedEffectConfigToken newToken,RenderArgs dstArgs,RenderArgs srcArgs) {
			LowerThreshold=newToken.GetProperty<Int32Property>(PropertyNames.LowerThreshold).Value;
			UpperThreshold=newToken.GetProperty<Int32Property>(PropertyNames.UpperThreshold).Value;
			float oldTolerance=Tolerance;
			Tolerance=newToken.GetProperty<Int32Property>(PropertyNames.Tolerance).Value;
			Tolerance*=Tolerance/toleranceMax/toleranceMax;
			ToleranceChanged=Tolerance==oldTolerance;
			base.OnSetRenderInfo(newToken,dstArgs,srcArgs);
			//new Thread(() => this.DelayRender(0)).Start();
			PdnRegion selection=EnvironmentParameters.GetSelection(SrcArgs.Surface.Bounds);
			List<RectangleRef> selRects=RectanglesToRectangleRefs(selection.GetRegionScansInt());
			//selRects=(split into proper rois);
			//CustomOnRender(selRects);
			CustomOnRender(SplitSmall(selRects,selection.GetBoundsInt().Bottom/4));

		}

		void DelayRender(int timeout) {
			Thread.Sleep(timeout);

			//PdnRegion selection=EnvironmentParameters.GetSelection(SrcArgs.Surface.Bounds);
			//List<RectangleRef> selRects=RectanglesToRectangleRefs(selection.GetRegionScansInt());
			//CustomOnRender(SplitSmall(selRects,selection.GetBoundsInt().Bottom/4));
		}

		protected override void OnCustomizeConfigUIWindowProperties(PropertyCollection props) {
			props[ControlInfoPropertyNames.WindowTitle].Value="Cluster Clear";
			props[ControlInfoPropertyNames.WindowHelpContentType].Value=WindowHelpContentType.PlainText;
			props[ControlInfoPropertyNames.WindowHelpContent].Value=" v1.0\nCopyright ©2017 by \nAll rights reserved.";
			base.OnCustomizeConfigUIWindowProperties(props);
		}

		static List<RectangleRef> RectanglesToRectangleRefs(IEnumerable<Rectangle> orig) {
			List<RectangleRef> rects=new List<RectangleRef>();
			orig.ForEach(r => rects.Add(new RectangleRef(r)));
			return rects;
		}

		static List<RectangleRef> SplitSmall(IEnumerable<RectangleRef> orig,int optRectHeight) {
			List<RectangleRef> rects=new List<RectangleRef>();
			foreach(RectangleRef rect in orig) {
				if(rect.Height>optRectHeight) {
					int maxY=rect.Bottom-optRectHeight;
					int y = rect.Y;
					for(;y<maxY;y+=optRectHeight) {
						rects.Add(new RectangleRef(rect.X,y,rect.Width,optRectHeight));
					}
					rects.Add(new RectangleRef(rect.X,y,rect.Width,rect.Bottom-y));
				}
				else {
					rects.Add(rect);
				}
			}
			return rects;
		}

		unsafe void CustomOnRender(IEnumerable<RectangleRef> rois) {
			if(ToleranceChanged||!ClustersFinished) {
				SynchronizedCollection<List<RectangleRef>> allRanges=new SynchronizedCollection<List<RectangleRef>>();
				Parallel.ForEach(rois,rect => {
					CleanUp(rect);
					List<RectangleRef> ranges=FindRanges(rect);
					allRanges.Add(ranges);
				});
				clusters=ClusterRanges(allRanges);
			}
			Parallel.ForEach(clusters,clust => {
				RenderCluster(DstArgs.Surface,clust);
			});
		}

		//will be changed to manual multithreading
		protected override unsafe void OnRender(Rectangle[] rois,int startIndex,int length) {
			return;
		}

		void CleanUp(Surface dst,Surface src,RectangleRef rect) {
			int bot=rect.Bottom,right=rect.Right;
			for(int y = rect.Top;y<bot;++y) {
				for(int x = rect.Left;x<right;++x) {
					dst[x,y]=src[x,y];
				}
			}
		}

		void CleanUp(RectangleRef r) {
			Surface dst=DstArgs.Surface,src=SrcArgs.Surface;
			int ymax=r.Bottom;
			int xmax=r.Right;
			for(int y = r.Top;y<ymax;++y) {
				for(int x = r.Left;x<xmax;++x) {
					dst[x,y]=src[x,y];
				}
			}
		}

		#region UICode
		IntSliderControl LowerThreshold = 0;
		IntSliderControl UpperThreshold = 50;
		float Tolerance = -5;
		#endregion

		List<RectangleRef> FindRanges(RectangleRef rect) {
			Surface src=SrcArgs.Surface;
			ColorBgra PrimaryColor=EnvironmentParameters.PrimaryColor;
			List<RectangleRef> ranges=new List<RectangleRef>();
			byte rangeFound=0;
			int rangeStart=0,rangeEnd=0;
			for(int y = rect.Top;y<rect.Bottom;++y) {
				if(IsCancelRequested) goto endloop;
				for(int x = rect.Left;x<rect.Right;++x) {
					switch(rangeFound) {
						case 0: {
								if(ColorPercentage(src[x,y],PrimaryColor)<=Tolerance) {
									rangeFound=1;
									rangeStart=x;
								}
								break;
							}
						case 1: {
								if(ColorPercentage(src[x,y],PrimaryColor)>Tolerance) {
									rangeFound=2;
									rangeEnd=x;
									goto case 2;
								}
								break;
							}
						case 2: {
								ranges.Add(new RectangleRef(rangeStart,y,rangeEnd-rangeStart,1));
								rangeFound=0;
								break;
							}
					}
				}
				if(1==rangeFound) {
					ranges.Add(new RectangleRef(rangeStart,y,rect.Right-rangeStart,1));
					rangeFound=0;
				}
			}
			endloop:
			CompressRanges(ranges);
			return ranges;
		}

		static void CompressRanges(List<RectangleRef> ranges) {
			ranges.Sort();
			for(int i = 1;i<ranges.Count();++i) {
				if(ranges[i-1].rect.Left==ranges[i].rect.Left&&ranges[i-1].rect.Right==ranges[i].rect.Right&&ranges[i-1].rect.Bottom==ranges[i].rect.Top) {
					ranges[i-1]=new RectangleRef(ranges[i-1].rect.Location,new Size(ranges[i].rect.Width,ranges[i-1].rect.Height+ranges[i].rect.Height));
					ranges.RemoveAt(i--);
				} /*
				else if(ranges[i-1].r==ranges[i].r) {
					ranges.RemoveAt(--i);
				}*/
			}
		}

		List<Cluster> ClusterRanges(IEnumerable<List<RectangleRef>> ranges) {
			ClustersFinished=false;
			List<ClusterTestNode> tests=new List<ClusterTestNode>();
			ranges.ForEach(rrl => {
				rrl.ForEach(rr => {
					ClusterPart toAdd=new ClusterPart(rr);
					tests.Add(new ClusterTestNode(toAdd,true));
					tests.Add(new ClusterTestNode(toAdd,false));
				}
				);
			});
			tests.Sort((a,b) => {//sort by y, if ys are same, bottoms are first
				int ydif=a.Y-b.Y;
				if(0!=ydif) return ydif;
				return a.IsTop.CompareTo(b.IsTop);
			});
			//int index=0;
			//tests.ForEach(ctn => Console.WriteLine(index+++"\t"+(ctn.IsTop ? "Top" : "Bottom")+":\t"+ctn.Parent.Rectangle));
			List<Cluster> Clusters=new List<Cluster>();
			Stack<int> searchStack=new Stack<int>();
			//ranges.Sort((a,b) => a.r.Y-b.r.Y);
			//n^2 can change to array of bottoms and tops with references to parent RectangleRef,sort by y (nlogn)
			int max=tests.Count;
			for(int i = max-1;i>=0;--i) {
				ClusterTestNode CurrentNode=tests[i];
				if(!CurrentNode.IsTop) continue;//if it is a bottom or is already in a cluster, nothing is done
				if(CurrentNode.Parent.Cluster==null) {
					//Console.WriteLine("\nTesting "+CurrentNode.Parent.Rectangle);
					Cluster CurrentCluster=new Cluster();
					Clusters.Add(CurrentCluster);
					searchStack.Push(i);
					while(searchStack.Count>0) {//search for contacts
						if(IsCancelRequested) goto endloop;
						int searchIndex=searchStack.Pop();
						ClusterTestNode SearchNode=tests[searchIndex];
						//Console.WriteLine("\tSearch Seed: "+SearchNode.Parent.Rectangle);
						if(SearchNode.Parent.Cluster!=null) continue;
						SearchNode.Parent.Cluster=CurrentCluster;
						CurrentCluster.Ranges.Add(SearchNode.Parent.Rectangle);
						//search up for bottoms
						for(int s = searchIndex-1;s>=0;--s) {
							if(!tests[s].IsTop) {
								if(tests[s].Y==SearchNode.Parent.Rectangle.Top&&tests[s].Parent.Rectangle.OverlapsX(SearchNode.Parent.Rectangle)) {
									searchStack.Push(s);
								}
								else if(tests[s].Y<SearchNode.Parent.Rectangle.Top) {
									//Console.WriteLine("\t\tStopped at "+s);
									break;
								}
							}
						}
						//search down for tops
						for(int s = searchIndex+1;s<max;++s) {
							if(tests[s].IsTop) {
								if(tests[s].Y==SearchNode.Parent.Rectangle.Bottom&&tests[s].Parent.Rectangle.OverlapsX(SearchNode.Parent.Rectangle)) {
									searchStack.Push(s);
								}
								else if(tests[s].Y>SearchNode.Parent.Rectangle.Bottom) {
									//Console.WriteLine("\t\tStopped at "+s);
									break;
								}
							}
						}
					}
				}
			}
			ClustersFinished=true;
			endloop:
			return Clusters;
		}

		class ClusterPart {
			public Cluster Cluster=null;
			public readonly RectangleRef Rectangle;
			public ClusterPart(RectangleRef rr) {
				Rectangle=rr;
			}
		}

		class ClusterTestNode {
			public readonly ClusterPart Parent;
			public readonly bool IsTop;
			public readonly int Y;
			public ClusterTestNode(ClusterPart Parent,bool Top) {
				this.Parent=Parent;
				this.IsTop=Top;
				Y=Top ? Parent.Rectangle.Top : Parent.Rectangle.Bottom;
			}
		}

		class ScanRange {
			public int left,right,y,direction;
			public ScanRange(int left,int right,int y,int direction) {
				this.left=left;
				this.right=right;
				this.y=y;
				this.direction=direction;
			}
		}

		class RectangleRef:IComparable {
			public Rectangle rect;

			public int Left { get { return rect.Left; } }
			public int Right { get { return rect.Right; } }
			public int Bottom { get { return rect.Bottom; } }
			public int Top { get { return rect.Top; } }
			public int X { get { return rect.X; } }
			public int Y { get { return rect.Y; } }
			public int Width { get { return rect.Width; } }
			public int Height { get { return rect.Height; } }

			public RectangleRef(int x,int y,int width,int height) {
				rect=new Rectangle(x,y,width,height);
			}
			public RectangleRef(Point loc,Size size) {
				rect=new Rectangle(loc,size);
			}
			public RectangleRef(Rectangle r) {
				this.rect=r;
			}
			public Boolean Contains(int x,int y) {
				return Contains(new Point(x,y));
			}
			public Boolean Contains(Point p) {
				return rect.Contains(p);
			}
			public int Area() {
				return Width*Height;
			}
			public int CompareTo(object obj) {
				RectangleRef other = (RectangleRef)obj;
				int xdif = this.X-other.X;
				return (0!=xdif) ? xdif : (this.Y-other.Y);
			}
			public override string ToString() {
				return rect.ToString();
			}
			public bool IsBorderingVert(RectangleRef other) {
				return (other.Bottom==Top||other.Top==Bottom)&&OverlapsX(other);
			}
			public bool OverlapsX(RectangleRef other) {
				return (other.Left>=Left&&other.Left<Right)
						||
						(other.Right>Left&&other.Right<=Right)
						||
						(other.Left<Left&&other.Right>Right);
			}
		}

		class Cluster {
			public List<RectangleRef> Ranges;
			//private bool sorted=false;

			public Cluster() {
				Ranges=new List<RectangleRef>();
			}

			public int NumPixels {
				get {
					int sum = 0;
					foreach(RectangleRef r in Ranges) sum+=r.Area();
					return sum;
				}
			}

			public RectangleRef Contains(Point p) {
				//if(!sorted) { ranges.Sort(); sorted=true; }
				foreach(RectangleRef r in Ranges) {
					if(r.Contains(p)) { return r; }
					if(r.rect.Left>p.X) { return null; }
				}
				return null;
			}

			public void Create(Point seed,Surface src,RectangleRef[] limits,ColorBgra color,float Tolerance,ClusterClearEffectPlugin controller,bool[,] safePoints) {
				Ranges.Clear();
				int xL = seed.X;
				while(xL>=0&&ColorPercentage(color,src[seed.X,seed.Y])<=Tolerance) {
					--xL;
				}
				++xL;
				int xR = seed.X+1;
				int maxR = src.Width;
				while(xR<maxR&&ColorPercentage(color,src[xR,seed.Y])<=Tolerance) {
					++xR;
				}
				--xR;
				Ranges.Add(new RectangleRef(xL,seed.Y,xR-xL+1,1));

				Stack<ScanRange> scanRanges = new Stack<ScanRange>();
				scanRanges.Push(new ScanRange(xL,xR,seed.Y,1));
				scanRanges.Push(new ScanRange(xL,xR,seed.Y,-1));
				int xMin = 0;
				int xMax = src.Width-1;
				int yMin = 0;
				int yMax = src.Height-1;
				ScanRange r;
				int sleft;
				int sright;
				while(scanRanges.Count!=0) {
					if(controller.IsCancelRequested) return;
					r=scanRanges.Pop();
					//scan left
					for(sleft=r.left-1;sleft>=xMin&&ColorPercentage(color,src[sleft,r.y])<=Tolerance;--sleft) {
						safePoints[sleft,r.y]=true;
					}
					++sleft;

					//scan right
					for(sright=r.right+1;sright<=xMax&&ColorPercentage(color,src[sright,r.y])<=Tolerance;++sright) {
						safePoints[sright,r.y]=true;
					}
					--sright;
					Ranges.Add(new RectangleRef(sleft,r.y,sright-sleft,1));

					//scan in same direction vertically
					bool rangeFound = false;
					int rangeStart = 0;
					int newy = r.y+r.direction;
					if(newy>=yMin&&newy<=yMax) {
						xL=sleft;
						while(xL<=sright) {
							for(;xL<=sright;++xL) {
								if(ColorPercentage(color,src[xL,newy])<=Tolerance) {
									safePoints[xL,newy]=true;
									rangeFound=true;
									rangeStart=xL++;
									break;
								}
							}
							for(;xL<=sright;++xL) {
								if(ColorPercentage(color,src[xL,newy])>Tolerance) {
									break;
								}
								safePoints[xL,newy]=true;
							}
							if(rangeFound) {
								rangeFound=false;
								scanRanges.Push(new ScanRange(rangeStart,xL-1,newy,r.direction));
							}
						}
					}

					//scan opposite direction vertically
					newy=r.y-r.direction;
					if(newy>=yMin&&newy<=yMax) {
						xL=sleft;
						while(xL<r.left) {
							for(;xL<r.left;++xL) {
								if(ColorPercentage(color,src[xL,newy])<=Tolerance) {
									safePoints[xL,newy]=true;
									rangeFound=true;
									rangeStart=xL++;
									break;
								}
							}
							for(;xL<r.left;++xL) {
								if(ColorPercentage(color,src[xL,newy])>Tolerance)
									break;
								safePoints[xL,newy]=true;
							}
							if(rangeFound) {
								rangeFound=false;
								scanRanges.Push(new ScanRange(rangeStart,xL-1,newy,-r.direction));
							}
						}
						xL=r.right+1;
						while(xL<=sright) {
							for(;xL<=sright;++xL) {
								if(ColorPercentage(color,src[xL,newy])<=Tolerance) {
									safePoints[xL,newy]=true;
									rangeFound=true;
									rangeStart=xL++;
									break;
								}
							}
							for(;xL<=sright;++xL) {
								if(ColorPercentage(color,src[xL,newy])>Tolerance)
									break;
								safePoints[xL,newy]=true;
							}
							if(rangeFound) {
								rangeFound=false;
								scanRanges.Push(new ScanRange(rangeStart,xL-1,newy,-r.direction));
							}
						}
					}
				}
				CompressRanges();
			}

			public void CompressRanges() {
				ClusterClearEffectPlugin.CompressRanges(Ranges);
			}
		}

		void RenderCluster(Surface dst,Cluster cluster) {
			ColorBgra SecondaryColor = EnvironmentParameters.SecondaryColor;
			int clusterSize = cluster.NumPixels;
			//bool properSize = (clusterSize>=LowerThreshold&&clusterSize<=UpperThreshold);
			if(clusterSize>=LowerThreshold&&clusterSize<=UpperThreshold) {
				foreach(RectangleRef r in cluster.Ranges) {
					for(int y = r.rect.Top;y<r.rect.Bottom;++y) {
						if(IsCancelRequested) return;
						for(int x = r.rect.Left;x<r.rect.Right;++x) {
							dst[x,y]=SecondaryColor;
						}
					}
				}
			}
		}

		const float maxDif = 255.0f*255.0f*3.0f;
		static float ColorPercentage(ColorBgra a,ColorBgra b) {
			var dR = (float)(a.R-b.R);
			var dG = (float)(a.G-b.G);
			var dB = (float)(a.B-b.G);
			return (dR*dR+dG*dG+dB*dB)/maxDif;
		}
	}
}
