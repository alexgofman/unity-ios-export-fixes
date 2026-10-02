# Stand-in for CocoaPods, used by RubyChecks.cs.
#
# Evaluates a Podfile against a minimal copy of the Podfile DSL, then runs its post_install hook
# against fake pod targets. That checks two things without installing CocoaPods: the patched file
# is valid Ruby, and the inserted hook changes the build settings the way its comment says.
#
# Usage: ruby podfile_harness.rb <Podfile> [--multi | --incremental] [<deployment target>...]
#   Each deployment target stands for one fake pod target; "-" means the target has no
#   IPHONEOS_DEPLOYMENT_TARGET setting at all.
#   --multi        every pod target sits in a project of its own, as with the CocoaPods install
#                  option generate_multiple_pod_projects
#   --incremental  no project was written at all, as in an incremental install with nothing to do:
#                  pods_project is nil and generated_projects is empty
#
# Output, one record per line, fields separated by "|":
#   pod|<target>|<pod name>         every pod declaration with the target block it sits in
#   hook|<yes or no>                whether the Podfile defines a post_install hook
#   target|<index>|<KEY=VALUE>...   build settings of each fake pod target after the hook ran
require 'rubygems'

Config = Struct.new(:build_settings)
Target = Struct.new(:name, :build_configurations)
Project = Struct.new(:targets)
Installer = Struct.new(:pods_project, :generated_projects)

class PodfileStandIn
  attr_reader :pods, :hook

  def initialize
    @pods = []
    @open_targets = []
    @hook = nil
  end

  def target(name, *)
    @open_targets.push(name)
    yield if block_given?
  ensure
    @open_targets.pop
  end
  alias abstract_target target

  def pod(name, *)
    @pods << [@open_targets.last || '(root)', name]
  end

  def post_install(&block)
    # Same rule and message as cocoapods-core.
    raise 'Specifying multiple `post_install` hooks is unsupported.' if @hook

    @hook = block
  end

  # The rest of the DSL (source, platform, use_frameworks!, inherit!, ...) is accepted and ignored.
  def method_missing(_name, *_args)
    nil
  end

  def respond_to_missing?(_name, _include_private = false)
    true
  end
end

podfile_path, *arguments = ARGV
multi = !arguments.delete('--multi').nil?
incremental = !arguments.delete('--incremental').nil?

podfile = PodfileStandIn.new
podfile.instance_eval(File.read(podfile_path), podfile_path)

targets = arguments.each_with_index.map do |value, index|
  settings = value == '-' ? {} : { 'IPHONEOS_DEPLOYMENT_TARGET' => value }
  Target.new("Pod#{index}", [Config.new(settings.dup), Config.new(settings.dup)])
end

installer =
  if incremental
    Installer.new(nil, [])
  elsif multi
    container = Project.new([])
    Installer.new(container, [container] + targets.map { |target| Project.new([target]) })
  else
    project = Project.new(targets)
    Installer.new(project, [project])
  end
podfile.hook.call(installer) if podfile.hook

podfile.pods.each { |target, name| puts "pod|#{target}|#{name}" }
puts "hook|#{podfile.hook ? 'yes' : 'no'}"
targets.each_with_index do |target, index|
  per_configuration = target.build_configurations.map do |config|
    config.build_settings.sort.map { |key, value| "#{key}=#{value}" }.join('|')
  end
  raise "configurations of target #{index} differ" unless per_configuration.uniq.size == 1

  puts ["target", index, per_configuration.first].reject { |field| field.to_s.empty? }.join('|')
end
